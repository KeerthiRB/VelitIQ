using System.Numerics.Tensors;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using VelitIQ.Core.Configuration;
using VelitIQ.Core.DTOs;
using VelitIQ.Core.Models;

namespace VelitIQ.Infrastructure.Data;

public sealed class PostgresKnowledgeStore
{
    private readonly DatabaseOptions _options;
    private readonly ILogger<PostgresKnowledgeStore> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public bool IsPgVectorEnabled { get; private set; }

    static PostgresKnowledgeStore()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
    }

    public PostgresKnowledgeStore(IOptions<DatabaseOptions> options, ILogger<PostgresKnowledgeStore> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public NpgsqlConnection CreateConnection() => new(_options.ConnectionString);

    public async Task InitializeSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        // 1. Auto-detect pgvector availability (enabled on Azure PostgreSQL or compiled on Windows Server; falls back to SIMD + real[] otherwise)
        try
        {
            await using var extCmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector;", conn);
            await extCmd.ExecuteNonQueryAsync(ct);
            IsPgVectorEnabled = true;
            _logger.LogInformation("PostgreSQL 'vector' (pgvector) extension is ENABLED. Using native HNSW cosine vector index.");
        }
        catch (Exception ex)
        {
            IsPgVectorEnabled = false;
            _logger.LogInformation("PostgreSQL 'vector' extension not installed on this host ({Message}). Using native PostgreSQL real[] + .NET 10 SIMD TensorPrimitives cosine similarity + GIN tsvector hybrid search.", ex.Message);
        }

        const string ddl = """
            CREATE TABLE IF NOT EXISTS departments (
                id UUID PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                code TEXT NOT NULL UNIQUE,
                description TEXT NOT NULL DEFAULT '',
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS teams (
                id UUID PRIMARY KEY,
                department_id UUID NOT NULL REFERENCES departments(id) ON DELETE CASCADE,
                name TEXT NOT NULL,
                slug TEXT NOT NULL UNIQUE,
                description TEXT NOT NULL DEFAULT '',
                ad_group_name TEXT NOT NULL DEFAULT '',
                entra_group_id TEXT NOT NULL DEFAULT '',
                manager_email TEXT NOT NULL DEFAULT '',
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS app_users (
                id UUID PRIMARY KEY,
                username TEXT NOT NULL UNIQUE,
                email TEXT NOT NULL UNIQUE,
                display_name TEXT NOT NULL,
                job_title TEXT NOT NULL DEFAULT '',
                password_hash TEXT NULL,
                auth_source TEXT NOT NULL DEFAULT 'Local',
                role INT NOT NULL DEFAULT 0,
                primary_team_id UUID NULL REFERENCES teams(id) ON DELETE SET NULL,
                additional_team_ids UUID[] NOT NULL DEFAULT '{}',
                is_active BOOLEAN NOT NULL DEFAULT TRUE,
                last_login_at TIMESTAMPTZ NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS departing_users (
                id UUID PRIMARY KEY,
                user_id UUID NULL REFERENCES app_users(id) ON DELETE SET NULL,
                department_id UUID NOT NULL REFERENCES departments(id) ON DELETE CASCADE,
                team_id UUID NOT NULL REFERENCES teams(id) ON DELETE CASCADE,
                full_name TEXT NOT NULL,
                email TEXT NOT NULL,
                employee_id TEXT NOT NULL DEFAULT '',
                job_title TEXT NOT NULL DEFAULT '',
                exit_date DATE NOT NULL,
                successor_name TEXT NOT NULL DEFAULT '',
                successor_email TEXT NOT NULL DEFAULT '',
                status INT NOT NULL DEFAULT 1,
                readiness_score INT NOT NULL DEFAULT 0,
                systems_owned TEXT[] NOT NULL DEFAULT '{}',
                recurring_schedules TEXT[] NOT NULL DEFAULT '{}',
                key_contacts TEXT[] NOT NULL DEFAULT '{}',
                executive_summary TEXT NOT NULL DEFAULT '',
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS knowledge_documents (
                id UUID PRIMARY KEY,
                department_id UUID NOT NULL REFERENCES departments(id) ON DELETE CASCADE,
                team_id UUID NOT NULL REFERENCES teams(id) ON DELETE CASCADE,
                departing_user_id UUID NOT NULL REFERENCES departing_users(id) ON DELETE CASCADE,
                uploaded_by_user_id UUID NULL REFERENCES app_users(id) ON DELETE SET NULL,
                title TEXT NOT NULL,
                original_file_name TEXT NOT NULL,
                storage_path TEXT NOT NULL,
                content_type TEXT NOT NULL DEFAULT 'application/octet-stream',
                file_size_bytes BIGINT NOT NULL DEFAULT 0,
                sha256_hash TEXT NOT NULL DEFAULT '',
                format INT NOT NULL,
                category INT NOT NULL DEFAULT 0,
                visibility INT NOT NULL DEFAULT 0,
                tags TEXT[] NOT NULL DEFAULT '{}',
                status INT NOT NULL DEFAULT 0,
                status_message TEXT NOT NULL DEFAULT '',
                summary TEXT NOT NULL DEFAULT '',
                page_or_sheet_count INT NOT NULL DEFAULT 0,
                chunk_count INT NOT NULL DEFAULT 0,
                formula_count INT NOT NULL DEFAULT 0,
                diagram_or_image_count INT NOT NULL DEFAULT 0,
                secret_warning_count INT NOT NULL DEFAULT 0,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                processed_at TIMESTAMPTZ NULL
            );

            CREATE TABLE IF NOT EXISTS document_chunks (
                id UUID PRIMARY KEY,
                document_id UUID NOT NULL REFERENCES knowledge_documents(id) ON DELETE CASCADE,
                department_id UUID NOT NULL,
                team_id UUID NOT NULL,
                departing_user_id UUID NOT NULL,
                visibility INT NOT NULL DEFAULT 0,
                chunk_index INT NOT NULL,
                source_type TEXT NOT NULL DEFAULT 'DocumentBody',
                section_path TEXT NOT NULL DEFAULT '',
                page_or_sheet_name TEXT NOT NULL DEFAULT '',
                content TEXT NOT NULL,
                contextual_content TEXT NOT NULL,
                embedding REAL[] NOT NULL DEFAULT '{}',
                token_estimate INT NOT NULL DEFAULT 0,
                metadata_json JSONB NOT NULL DEFAULT '{}'::jsonb,
                search_vector TSVECTOR GENERATED ALWAYS AS (to_tsvector('english', coalesce(contextual_content, ''))) STORED,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE INDEX IF NOT EXISTS idx_chunks_search_vector ON document_chunks USING GIN (search_vector);
            CREATE INDEX IF NOT EXISTS idx_chunks_team_user ON document_chunks (team_id, departing_user_id, visibility);
            CREATE INDEX IF NOT EXISTS idx_chunks_document ON document_chunks (document_id, chunk_index);

            CREATE TABLE IF NOT EXISTS tabular_sheets (
                id UUID PRIMARY KEY,
                document_id UUID NOT NULL REFERENCES knowledge_documents(id) ON DELETE CASCADE,
                department_id UUID NOT NULL,
                team_id UUID NOT NULL,
                departing_user_id UUID NOT NULL,
                visibility INT NOT NULL DEFAULT 0,
                sheet_name TEXT NOT NULL,
                sheet_index INT NOT NULL DEFAULT 0,
                row_count INT NOT NULL DEFAULT 0,
                column_count INT NOT NULL DEFAULT 0,
                columns_json JSONB NOT NULL DEFAULT '[]'::jsonb,
                formulas_json JSONB NOT NULL DEFAULT '[]'::jsonb,
                summary_markdown TEXT NOT NULL DEFAULT '',
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS tabular_rows (
                id BIGSERIAL PRIMARY KEY,
                sheet_id UUID NOT NULL REFERENCES tabular_sheets(id) ON DELETE CASCADE,
                document_id UUID NOT NULL REFERENCES knowledge_documents(id) ON DELETE CASCADE,
                row_index INT NOT NULL,
                row_data_json JSONB NOT NULL DEFAULT '{}'::jsonb,
                search_text TEXT NOT NULL DEFAULT ''
            );

            CREATE INDEX IF NOT EXISTS idx_tabular_rows_sheet ON tabular_rows (sheet_id, row_index);
            CREATE INDEX IF NOT EXISTS idx_tabular_rows_jsonb ON tabular_rows USING GIN (row_data_json);

            CREATE TABLE IF NOT EXISTS handover_gap_questions (
                id UUID PRIMARY KEY,
                departing_user_id UUID NOT NULL REFERENCES departing_users(id) ON DELETE CASCADE,
                team_id UUID NOT NULL REFERENCES teams(id) ON DELETE CASCADE,
                document_id UUID NULL REFERENCES knowledge_documents(id) ON DELETE CASCADE,
                category TEXT NOT NULL DEFAULT 'UndocumentedDependency',
                priority TEXT NOT NULL DEFAULT 'High',
                question TEXT NOT NULL,
                context_snippet TEXT NOT NULL DEFAULT '',
                answer TEXT NULL,
                answered_by_name TEXT NULL,
                answered_at TIMESTAMPTZ NULL,
                status INT NOT NULL DEFAULT 0,
                indexed_chunk_id UUID NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS secret_scan_findings (
                id UUID PRIMARY KEY,
                document_id UUID NOT NULL REFERENCES knowledge_documents(id) ON DELETE CASCADE,
                rule_name TEXT NOT NULL,
                severity INT NOT NULL DEFAULT 1,
                location TEXT NOT NULL DEFAULT '',
                redacted_preview TEXT NOT NULL DEFAULT '',
                is_acknowledged BOOLEAN NOT NULL DEFAULT FALSE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS audit_logs (
                id UUID PRIMARY KEY,
                timestamp TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                actor_user_id UUID NULL,
                actor_email TEXT NOT NULL DEFAULT '',
                actor_role TEXT NOT NULL DEFAULT '',
                action TEXT NOT NULL,
                resource_type TEXT NOT NULL DEFAULT '',
                resource_id TEXT NOT NULL DEFAULT '',
                team_id UUID NULL,
                details_json JSONB NOT NULL DEFAULT '{}'::jsonb,
                ip_address TEXT NOT NULL DEFAULT ''
            );

            CREATE INDEX IF NOT EXISTS idx_audit_logs_ts ON audit_logs (timestamp DESC);
            """;

        await using (var cmd = new NpgsqlCommand(ddl, conn))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        if (IsPgVectorEnabled)
        {
            var dim = _options.EmbeddingDimensions;
            var pgVectorDdl = $"""
                ALTER TABLE document_chunks ADD COLUMN IF NOT EXISTS embedding_vec vector({dim});
                CREATE INDEX IF NOT EXISTS idx_chunks_embedding_hnsw ON document_chunks USING hnsw (embedding_vec vector_cosine_ops);
                """;
            await using var vecCmd = new NpgsqlCommand(pgVectorDdl, conn);
            await vecCmd.ExecuteNonQueryAsync(ct);
        }
    }

    // ==================== ORGANIZATION & USERS ====================

    public async Task<IReadOnlyList<Department>> GetDepartmentsAsync(CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        var rows = await conn.QueryAsync<Department>(
            new CommandDefinition("SELECT * FROM departments ORDER BY name;", cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Department> CreateDepartmentAsync(Department dept, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO departments (id, name, code, description, created_at)
            VALUES (@Id, @Name, @Code, @Description, @CreatedAt)
            ON CONFLICT (code) DO UPDATE SET name = EXCLUDED.name, description = EXCLUDED.description;
            """, dept, cancellationToken: ct));
        return dept;
    }

    public async Task<IReadOnlyList<Team>> GetTeamsAsync(Guid? departmentId = null, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        const string sql = """
            SELECT t.*, d.name AS department_name
            FROM teams t
            JOIN departments d ON d.id = t.department_id
            WHERE (@DepartmentId IS NULL OR t.department_id = @DepartmentId)
            ORDER BY d.name, t.name;
            """;
        var rows = await conn.QueryAsync<Team>(
            new CommandDefinition(sql, new { DepartmentId = departmentId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Team?> GetTeamByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Team>(new CommandDefinition(
            """
            SELECT t.*, d.name AS department_name
            FROM teams t
            JOIN departments d ON d.id = t.department_id
            WHERE t.id = @Id;
            """, new { Id = id }, cancellationToken: ct));
    }

    public async Task<Team?> GetTeamBySlugAsync(string slug, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Team>(new CommandDefinition(
            """
            SELECT t.*, d.name AS department_name
            FROM teams t
            JOIN departments d ON d.id = t.department_id
            WHERE lower(t.slug) = lower(@Slug);
            """, new { Slug = slug }, cancellationToken: ct));
    }

    public async Task<Team> CreateTeamAsync(Team team, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO teams (id, department_id, name, slug, description, ad_group_name, entra_group_id, manager_email, created_at)
            VALUES (@Id, @DepartmentId, @Name, @Slug, @Description, @AdGroupName, @EntraGroupId, @ManagerEmail, @CreatedAt)
            ON CONFLICT (slug) DO UPDATE SET
                name = EXCLUDED.name,
                description = EXCLUDED.description,
                ad_group_name = EXCLUDED.ad_group_name,
                entra_group_id = EXCLUDED.entra_group_id,
                manager_email = EXCLUDED.manager_email;
            """, team, cancellationToken: ct));
        return (await GetTeamByIdAsync(team.Id, ct)) ?? team;
    }

    public async Task<AppUser?> GetUserByUsernameOrEmailAsync(string usernameOrEmail, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<AppUser>(new CommandDefinition(
            """
            SELECT * FROM app_users
            WHERE lower(email) = lower(@Val) OR lower(username) = lower(@Val)
            LIMIT 1;
            """, new { Val = usernameOrEmail }, cancellationToken: ct));
    }

    public async Task<AppUser?> GetUserByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<AppUser>(new CommandDefinition(
            "SELECT * FROM app_users WHERE id = @Id;", new { Id = id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AppUser>> GetAllAppUsersAsync(CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        var rows = await conn.QueryAsync<AppUser>(new CommandDefinition(
            "SELECT * FROM app_users ORDER BY role DESC, display_name;", cancellationToken: ct));
        return rows.ToList();
    }

    public async Task UpsertAppUserAsync(AppUser user, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO app_users (id, username, email, display_name, job_title, password_hash, auth_source, role, primary_team_id, additional_team_ids, is_active, last_login_at, created_at)
            VALUES (@Id, @Username, @Email, @DisplayName, @JobTitle, @PasswordHash, @AuthSource, @RoleInt, @PrimaryTeamId, @AdditionalTeamIds, @IsActive, @LastLoginAt, @CreatedAt)
            ON CONFLICT (email) DO UPDATE SET
                display_name = EXCLUDED.display_name,
                job_title = EXCLUDED.job_title,
                auth_source = EXCLUDED.auth_source,
                role = EXCLUDED.role,
                primary_team_id = EXCLUDED.primary_team_id,
                additional_team_ids = EXCLUDED.additional_team_ids,
                last_login_at = COALESCE(EXCLUDED.last_login_at, app_users.last_login_at);
            """,
            new
            {
                user.Id,
                user.Username,
                user.Email,
                user.DisplayName,
                user.JobTitle,
                user.PasswordHash,
                user.AuthSource,
                RoleInt = (int)user.Role,
                user.PrimaryTeamId,
                user.AdditionalTeamIds,
                user.IsActive,
                user.LastLoginAt,
                user.CreatedAt
            }, cancellationToken: ct));
    }

    public async Task UpdateLastLoginAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE app_users SET last_login_at = NOW() WHERE id = @Id;",
            new { Id = userId }, cancellationToken: ct));
    }

    // ==================== DEPARTING USER PROFILES ====================

    public async Task<IReadOnlyList<DepartingUserProfile>> GetDepartingUsersAsync(
        Guid? departmentId = null,
        Guid? teamId = null,
        CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        const string sql = """
            SELECT du.*, d.name AS department_name, t.name AS team_name
            FROM departing_users du
            JOIN departments d ON d.id = du.department_id
            JOIN teams t ON t.id = du.team_id
            WHERE (@DepartmentId IS NULL OR du.department_id = @DepartmentId)
              AND (@TeamId IS NULL OR du.team_id = @TeamId)
            ORDER BY du.exit_date ASC, du.full_name ASC;
            """;
        var rows = await conn.QueryAsync<DepartingUserProfile>(
            new CommandDefinition(sql, new { DepartmentId = departmentId, TeamId = teamId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<DepartingUserProfile?> GetDepartingUserByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<DepartingUserProfile>(new CommandDefinition(
            """
            SELECT du.*, d.name AS department_name, t.name AS team_name
            FROM departing_users du
            JOIN departments d ON d.id = du.department_id
            JOIN teams t ON t.id = du.team_id
            WHERE du.id = @Id;
            """, new { Id = id }, cancellationToken: ct));
    }

    public async Task<DepartingUserProfile> UpsertDepartingUserAsync(DepartingUserProfile profile, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO departing_users (
                id, user_id, department_id, team_id, full_name, email, employee_id, job_title,
                exit_date, successor_name, successor_email, status, readiness_score,
                systems_owned, recurring_schedules, key_contacts, executive_summary, created_at, updated_at
            ) VALUES (
                @Id, @UserId, @DepartmentId, @TeamId, @FullName, @Email, @EmployeeId, @JobTitle,
                @ExitDate, @SuccessorName, @SuccessorEmail, @StatusInt, @ReadinessScore,
                @SystemsOwned, @RecurringSchedules, @KeyContacts, @ExecutiveSummary, @CreatedAt, NOW()
            )
            ON CONFLICT (id) DO UPDATE SET
                full_name = EXCLUDED.full_name,
                job_title = EXCLUDED.job_title,
                exit_date = EXCLUDED.exit_date,
                successor_name = EXCLUDED.successor_name,
                successor_email = EXCLUDED.successor_email,
                status = EXCLUDED.status,
                readiness_score = EXCLUDED.readiness_score,
                systems_owned = EXCLUDED.systems_owned,
                recurring_schedules = EXCLUDED.recurring_schedules,
                key_contacts = EXCLUDED.key_contacts,
                executive_summary = EXCLUDED.executive_summary,
                updated_at = NOW();
            """,
            new
            {
                profile.Id,
                profile.UserId,
                profile.DepartmentId,
                profile.TeamId,
                profile.FullName,
                profile.Email,
                profile.EmployeeId,
                profile.JobTitle,
                ExitDate = profile.ExitDate.ToDateTime(TimeOnly.MinValue),
                profile.SuccessorName,
                profile.SuccessorEmail,
                StatusInt = (int)profile.Status,
                profile.ReadinessScore,
                profile.SystemsOwned,
                profile.RecurringSchedules,
                profile.KeyContacts,
                profile.ExecutiveSummary,
                profile.CreatedAt
            }, cancellationToken: ct));

        return (await GetDepartingUserByIdAsync(profile.Id, ct)) ?? profile;
    }

    // ==================== DOCUMENTS, CHUNKS & TABULAR DATA ====================

    public async Task<IReadOnlyList<KnowledgeDocument>> GetAccessibleDocumentsAsync(
        SecurityContext caller,
        Guid? departmentId = null,
        Guid? teamId = null,
        Guid? departingUserId = null,
        ArtifactFormat? format = null,
        CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        const string sql = """
            SELECT kd.*, d.name AS department_name, t.name AS team_name, du.full_name AS departing_user_name
            FROM knowledge_documents kd
            JOIN departments d ON d.id = kd.department_id
            JOIN teams t ON t.id = kd.team_id
            JOIN departing_users du ON du.id = kd.departing_user_id
            WHERE (@DepartmentId IS NULL OR kd.department_id = @DepartmentId)
              AND (@TeamId IS NULL OR kd.team_id = @TeamId)
              AND (@DepartingUserId IS NULL OR kd.departing_user_id = @DepartingUserId)
              AND (@FormatInt IS NULL OR kd.format = @FormatInt)
            ORDER BY kd.created_at DESC;
            """;

        var docs = await conn.QueryAsync<KnowledgeDocument>(new CommandDefinition(
            sql,
            new
            {
                DepartmentId = departmentId,
                TeamId = teamId,
                DepartingUserId = departingUserId,
                FormatInt = format.HasValue ? (int?)format.Value : null
            },
            cancellationToken: ct));

        return docs
            .Where(doc => caller.CanReadResource(doc.DepartmentId, doc.TeamId, doc.Visibility))
            .ToList();
    }

    public async Task<KnowledgeDocument?> GetDocumentByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<KnowledgeDocument>(new CommandDefinition(
            """
            SELECT kd.*, d.name AS department_name, t.name AS team_name, du.full_name AS departing_user_name
            FROM knowledge_documents kd
            JOIN departments d ON d.id = kd.department_id
            JOIN teams t ON t.id = kd.team_id
            JOIN departing_users du ON du.id = kd.departing_user_id
            WHERE kd.id = @Id;
            """, new { Id = id }, cancellationToken: ct));
    }

    public async Task UpsertDocumentAsync(KnowledgeDocument doc, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO knowledge_documents (
                id, department_id, team_id, departing_user_id, uploaded_by_user_id, title, original_file_name,
                storage_path, content_type, file_size_bytes, sha256_hash, format, category, visibility, tags,
                status, status_message, summary, page_or_sheet_count, chunk_count, formula_count,
                diagram_or_image_count, secret_warning_count, created_at, processed_at
            ) VALUES (
                @Id, @DepartmentId, @TeamId, @DepartingUserId, @UploadedByUserId, @Title, @OriginalFileName,
                @StoragePath, @ContentType, @FileSizeBytes, @Sha256Hash, @FormatInt, @CategoryInt, @VisibilityInt, @Tags,
                @StatusInt, @StatusMessage, @Summary, @PageOrSheetCount, @ChunkCount, @FormulaCount,
                @DiagramOrImageCount, @SecretWarningCount, @CreatedAt, @ProcessedAt
            )
            ON CONFLICT (id) DO UPDATE SET
                title = EXCLUDED.title,
                category = EXCLUDED.category,
                visibility = EXCLUDED.visibility,
                tags = EXCLUDED.tags,
                status = EXCLUDED.status,
                status_message = EXCLUDED.status_message,
                summary = EXCLUDED.summary,
                page_or_sheet_count = EXCLUDED.page_or_sheet_count,
                chunk_count = EXCLUDED.chunk_count,
                formula_count = EXCLUDED.formula_count,
                diagram_or_image_count = EXCLUDED.diagram_or_image_count,
                secret_warning_count = EXCLUDED.secret_warning_count,
                processed_at = EXCLUDED.processed_at;
            """,
            new
            {
                doc.Id,
                doc.DepartmentId,
                doc.TeamId,
                doc.DepartingUserId,
                doc.UploadedByUserId,
                doc.Title,
                doc.OriginalFileName,
                doc.StoragePath,
                doc.ContentType,
                doc.FileSizeBytes,
                doc.Sha256Hash,
                FormatInt = (int)doc.Format,
                CategoryInt = (int)doc.Category,
                VisibilityInt = (int)doc.Visibility,
                doc.Tags,
                StatusInt = (int)doc.Status,
                doc.StatusMessage,
                doc.Summary,
                doc.PageOrSheetCount,
                doc.ChunkCount,
                doc.FormulaCount,
                doc.DiagramOrImageCount,
                doc.SecretWarningCount,
                doc.CreatedAt,
                doc.ProcessedAt
            }, cancellationToken: ct));
    }

    public async Task UpdateDocumentVisibilityAsync(Guid documentId, VisibilityScope visibility, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE knowledge_documents SET visibility = @Vis WHERE id = @Id;
            UPDATE document_chunks SET visibility = @Vis WHERE document_id = @Id;
            UPDATE tabular_sheets SET visibility = @Vis WHERE document_id = @Id;
            """,
            new { Id = documentId, Vis = (int)visibility },
            cancellationToken: ct));
    }

    public async Task SaveChunksAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken ct = default)
    {
        if (chunks.Count == 0) return;

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        foreach (var chunk in chunks)
        {
            if (IsPgVectorEnabled && chunk.Embedding.Length > 0)
            {
                var vecLiteral = "[" + string.Join(",", chunk.Embedding.Select(v => v.ToString("G9", System.Globalization.CultureInfo.InvariantCulture))) + "]";
                await using var cmd = new NpgsqlCommand(
                    """
                    INSERT INTO document_chunks (
                        id, document_id, department_id, team_id, departing_user_id, visibility, chunk_index,
                        source_type, section_path, page_or_sheet_name, content, contextual_content,
                        embedding, embedding_vec, token_estimate, metadata_json, created_at
                    ) VALUES (
                        @id, @docId, @deptId, @teamId, @userId, @vis, @idx,
                        @srcType, @secPath, @pageName, @content, @ctxContent,
                        @emb, @vecLit::vector, @tokEst, @meta::jsonb, @createdAt
                    )
                    ON CONFLICT (id) DO UPDATE SET
                        content = EXCLUDED.content,
                        contextual_content = EXCLUDED.contextual_content,
                        embedding = EXCLUDED.embedding,
                        embedding_vec = EXCLUDED.embedding_vec;
                    """, conn, tx);
                AddChunkParameters(cmd, chunk);
                cmd.Parameters.AddWithValue("vecLit", vecLiteral);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            else
            {
                await using var cmd = new NpgsqlCommand(
                    """
                    INSERT INTO document_chunks (
                        id, document_id, department_id, team_id, departing_user_id, visibility, chunk_index,
                        source_type, section_path, page_or_sheet_name, content, contextual_content,
                        embedding, token_estimate, metadata_json, created_at
                    ) VALUES (
                        @id, @docId, @deptId, @teamId, @userId, @vis, @idx,
                        @srcType, @secPath, @pageName, @content, @ctxContent,
                        @emb, @tokEst, @meta::jsonb, @createdAt
                    )
                    ON CONFLICT (id) DO UPDATE SET
                        content = EXCLUDED.content,
                        contextual_content = EXCLUDED.contextual_content,
                        embedding = EXCLUDED.embedding;
                    """, conn, tx);
                AddChunkParameters(cmd, chunk);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }

        await tx.CommitAsync(ct);
    }

    private static void AddChunkParameters(NpgsqlCommand cmd, DocumentChunk chunk)
    {
        cmd.Parameters.AddWithValue("id", chunk.Id);
        cmd.Parameters.AddWithValue("docId", chunk.DocumentId);
        cmd.Parameters.AddWithValue("deptId", chunk.DepartmentId);
        cmd.Parameters.AddWithValue("teamId", chunk.TeamId);
        cmd.Parameters.AddWithValue("userId", chunk.DepartingUserId);
        cmd.Parameters.AddWithValue("vis", (int)chunk.Visibility);
        cmd.Parameters.AddWithValue("idx", chunk.ChunkIndex);
        cmd.Parameters.AddWithValue("srcType", chunk.SourceType);
        cmd.Parameters.AddWithValue("secPath", chunk.SectionPath);
        cmd.Parameters.AddWithValue("pageName", chunk.PageOrSheetName);
        cmd.Parameters.AddWithValue("content", chunk.Content);
        cmd.Parameters.AddWithValue("ctxContent", chunk.ContextualContent);
        cmd.Parameters.Add(new NpgsqlParameter("emb", NpgsqlDbType.Array | NpgsqlDbType.Real) { Value = chunk.Embedding });
        cmd.Parameters.AddWithValue("tokEst", chunk.TokenEstimate);
        cmd.Parameters.AddWithValue("meta", string.IsNullOrWhiteSpace(chunk.MetadataJson) ? "{}" : chunk.MetadataJson);
        cmd.Parameters.AddWithValue("createdAt", chunk.CreatedAt);
    }

    public async Task SaveTabularSheetAndRowsAsync(TabularSheet sheet, IReadOnlyList<TabularRow> rows, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO tabular_sheets (
                id, document_id, department_id, team_id, departing_user_id, visibility,
                sheet_name, sheet_index, row_count, column_count, columns_json, formulas_json, summary_markdown, created_at
            ) VALUES (
                @Id, @DocumentId, @DepartmentId, @TeamId, @DepartingUserId, @VisibilityInt,
                @SheetName, @SheetIndex, @RowCount, @ColumnCount, @ColumnsJson::jsonb, @FormulasJson::jsonb, @SummaryMarkdown, @CreatedAt
            );
            """,
            new
            {
                sheet.Id,
                sheet.DocumentId,
                sheet.DepartmentId,
                sheet.TeamId,
                sheet.DepartingUserId,
                VisibilityInt = (int)sheet.Visibility,
                sheet.SheetName,
                sheet.SheetIndex,
                sheet.RowCount,
                sheet.ColumnCount,
                sheet.ColumnsJson,
                sheet.FormulasJson,
                sheet.SummaryMarkdown,
                sheet.CreatedAt
            }, transaction: tx, cancellationToken: ct));

        foreach (var row in rows)
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO tabular_rows (sheet_id, document_id, row_index, row_data_json, search_text)
                VALUES (@sheetId, @docId, @rowIdx, @rowData::jsonb, @searchText);
                """, conn, tx);
            cmd.Parameters.AddWithValue("sheetId", sheet.Id);
            cmd.Parameters.AddWithValue("docId", sheet.DocumentId);
            cmd.Parameters.AddWithValue("rowIdx", row.RowIndex);
            cmd.Parameters.AddWithValue("rowData", row.RowDataJson);
            cmd.Parameters.AddWithValue("searchText", row.SearchText);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<DocumentChunk>> GetDocumentChunksAsync(Guid documentId, int startIndex = 0, int count = 15, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        var rows = await conn.QueryAsync<DocumentChunk>(new CommandDefinition(
            """
            SELECT id, document_id, department_id, team_id, departing_user_id, visibility, chunk_index,
                   source_type, section_path, page_or_sheet_name, content, contextual_content, token_estimate, created_at
            FROM document_chunks
            WHERE document_id = @DocumentId AND chunk_index >= @StartIndex
            ORDER BY chunk_index ASC
            LIMIT @Count;
            """,
            new { DocumentId = documentId, StartIndex = startIndex, Count = count },
            cancellationToken: ct));
        return rows.ToList();
    }

    // ==================== HYBRID VECTOR + LEXICAL RRF SEARCH ====================

    private sealed class CandidateChunkRow
    {
        public Guid ChunkId { get; set; }
        public Guid DocumentId { get; set; }
        public string DocumentTitle { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public int Format { get; set; }
        public int Visibility { get; set; }
        public Guid DepartmentId { get; set; }
        public Guid TeamId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public Guid DepartingUserId { get; set; }
        public string DepartingUserName { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public string SectionPath { get; set; } = string.Empty;
        public string PageOrSheetName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public float[] Embedding { get; set; } = [];
        public double LexicalRank { get; set; }
        public double? PgVectorCosineSimilarity { get; set; }
    }

    public async Task<IReadOnlyList<HybridSearchHitDto>> SearchHybridAsync(
        SecurityContext caller,
        HybridSearchRequest request,
        float[] queryEmbedding,
        CancellationToken ct = default)
    {
        await using var conn = CreateConnection();

        // Convert user natural language query into safe OR-joined PostgreSQL tsquery terms so exact acronyms/GL codes boost strongly
        var sanitizedTokens = System.Text.RegularExpressions.Regex
            .Matches(request.Query ?? string.Empty, @"[A-Za-z0-9_]{2,}")
            .Select(m => m.Value.ToLowerInvariant())
            .Distinct()
            .Take(16)
            .ToList();

        var orTsQuery = sanitizedTokens.Count > 0
            ? string.Join(" | ", sanitizedTokens.Select(t => $"{t}:*"))
            : "bank";

        List<CandidateChunkRow> candidates;

        if (IsPgVectorEnabled && queryEmbedding.Length > 0)
        {
            var vecLiteral = "[" + string.Join(",", queryEmbedding.Select(v => v.ToString("G9", System.Globalization.CultureInfo.InvariantCulture))) + "]";
            const string pgVecSql = """
                SELECT
                    c.id AS chunk_id,
                    c.document_id,
                    kd.title AS document_title,
                    kd.original_file_name,
                    kd.format,
                    c.visibility,
                    c.department_id,
                    c.team_id,
                    t.name AS team_name,
                    c.departing_user_id,
                    du.full_name AS departing_user_name,
                    c.source_type,
                    c.section_path,
                    c.page_or_sheet_name,
                    c.content,
                    ts_rank_cd(c.search_vector, to_tsquery('english', @OrTsQuery)) AS lexical_rank,
                    CASE WHEN c.embedding_vec IS NOT NULL THEN 1.0 - (c.embedding_vec <=> @VecLiteral::vector) ELSE 0.0 END AS pg_vector_cosine_similarity
                FROM document_chunks c
                JOIN knowledge_documents kd ON kd.id = c.document_id
                JOIN teams t ON t.id = c.team_id
                JOIN departing_users du ON du.id = c.departing_user_id
                WHERE (@DepartmentId IS NULL OR c.department_id = @DepartmentId)
                  AND (@TeamId IS NULL OR c.team_id = @TeamId)
                  AND (@DepartingUserId IS NULL OR c.departing_user_id = @DepartingUserId)
                  AND (@FormatInt IS NULL OR kd.format = @FormatInt)
                ORDER BY (CASE WHEN c.embedding_vec IS NOT NULL THEN (c.embedding_vec <=> @VecLiteral::vector) ELSE 1.0 END) ASC
                LIMIT 150;
                """;

            candidates = (await conn.QueryAsync<CandidateChunkRow>(new CommandDefinition(
                pgVecSql,
                new
                {
                    OrTsQuery = orTsQuery,
                    VecLiteral = vecLiteral,
                    request.DepartmentId,
                    request.TeamId,
                    request.DepartingUserId,
                    FormatInt = request.Format.HasValue ? (int?)request.Format.Value : null
                },
                cancellationToken: ct))).ToList();
        }
        else
        {
            const string simdSql = """
                SELECT
                    c.id AS chunk_id,
                    c.document_id,
                    kd.title AS document_title,
                    kd.original_file_name,
                    kd.format,
                    c.visibility,
                    c.department_id,
                    c.team_id,
                    t.name AS team_name,
                    c.departing_user_id,
                    du.full_name AS departing_user_name,
                    c.source_type,
                    c.section_path,
                    c.page_or_sheet_name,
                    c.content,
                    c.embedding,
                    ts_rank_cd(c.search_vector, to_tsquery('english', @OrTsQuery)) AS lexical_rank
                FROM document_chunks c
                JOIN knowledge_documents kd ON kd.id = c.document_id
                JOIN teams t ON t.id = c.team_id
                JOIN departing_users du ON du.id = c.departing_user_id
                WHERE (@DepartmentId IS NULL OR c.department_id = @DepartmentId)
                  AND (@TeamId IS NULL OR c.team_id = @TeamId)
                  AND (@DepartingUserId IS NULL OR c.departing_user_id = @DepartingUserId)
                  AND (@FormatInt IS NULL OR kd.format = @FormatInt);
                """;

            candidates = (await conn.QueryAsync<CandidateChunkRow>(new CommandDefinition(
                simdSql,
                new
                {
                    OrTsQuery = orTsQuery,
                    request.DepartmentId,
                    request.TeamId,
                    request.DepartingUserId,
                    FormatInt = request.Format.HasValue ? (int?)request.Format.Value : null
                },
                cancellationToken: ct))).ToList();
        }

        // Strict RBAC / Chinese-Wall Enforcement BEFORE ranking
        var authorized = candidates
            .Where(c => caller.CanReadResource(c.DepartmentId, c.TeamId, (VisibilityScope)c.Visibility))
            .ToList();

        if (authorized.Count == 0) return [];

        // Compute Vector Cosine Similarity (using pgvector result if available, or .NET 10 SIMD TensorPrimitives)
        var scored = authorized.Select(c =>
        {
            double vecSim = c.PgVectorCosineSimilarity ?? 0.0;
            if (!c.PgVectorCosineSimilarity.HasValue && queryEmbedding.Length > 0 && c.Embedding.Length == queryEmbedding.Length)
            {
                var sim = TensorPrimitives.CosineSimilarity(queryEmbedding, c.Embedding);
                vecSim = double.IsNaN(sim) ? 0.0 : sim;
            }
            // Boost AuthorGapAnswer chunks slightly when relevant since they represent verified author clarifications
            var boost = c.SourceType == "AuthorGapAnswer" ? 1.15 : 1.0;
            return (Row: c, VecSim: vecSim, LexScore: c.LexicalRank * boost);
        }).ToList();

        // Reciprocal Rank Fusion (RRF, k = 60) combining Dense Semantic Rank + PostgreSQL tsvector Lexical Rank
        const double rrfK = 60.0;
        var vecRanks = scored
            .OrderByDescending(x => x.VecSim)
            .Select((x, idx) => (x.Row.ChunkId, Rank: idx + 1))
            .ToDictionary(x => x.ChunkId, x => x.Rank);

        var lexRanks = scored
            .OrderByDescending(x => x.LexScore)
            .Select((x, idx) => (x.Row.ChunkId, Rank: idx + 1))
            .ToDictionary(x => x.ChunkId, x => x.Rank);

        var topK = Math.Clamp(request.TopK, 1, 25);

        return scored
            .Select(x =>
            {
                var vRank = vecRanks[x.Row.ChunkId];
                var lRank = lexRanks[x.Row.ChunkId];
                var lexicalComponent = x.LexScore > 0 ? (1.0 / (rrfK + lRank)) : 0.0;
                var vectorComponent = 1.0 / (rrfK + vRank);
                var authorBoost = x.Row.SourceType == "AuthorGapAnswer" && x.LexScore > 0 ? 1.35 : 1.0;
                var fused = (vectorComponent + (lexicalComponent * 1.25)) * authorBoost;

                return new HybridSearchHitDto(
                    x.Row.ChunkId,
                    x.Row.DocumentId,
                    x.Row.DocumentTitle,
                    x.Row.OriginalFileName,
                    (ArtifactFormat)x.Row.Format,
                    (VisibilityScope)x.Row.Visibility,
                    x.Row.TeamId,
                    x.Row.TeamName,
                    x.Row.DepartingUserId,
                    x.Row.DepartingUserName,
                    x.Row.SourceType,
                    x.Row.SectionPath,
                    x.Row.PageOrSheetName,
                    x.Row.Content,
                    Math.Round(x.VecSim, 4),
                    Math.Round(x.LexScore, 4),
                    Math.Round(fused, 6));
            })
            .OrderByDescending(h => h.FusedScore)
            .ThenByDescending(h => h.LexicalScore)
            .ThenByDescending(h => h.VectorSimilarity)
            .Take(topK)
            .ToList();
    }

    // ==================== TABULAR SPREADSHEET (CSV / EXCEL) QUERY ENGINE ====================

    public async Task<IReadOnlyList<TabularSheet>> GetAccessibleSheetsAsync(
        SecurityContext caller,
        Guid? teamId = null,
        Guid? departingUserId = null,
        Guid? documentId = null,
        CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        const string sql = """
            SELECT s.*, kd.title AS document_title
            FROM tabular_sheets s
            JOIN knowledge_documents kd ON kd.id = s.document_id
            WHERE (@TeamId IS NULL OR s.team_id = @TeamId)
              AND (@DepartingUserId IS NULL OR s.departing_user_id = @DepartingUserId)
              AND (@DocumentId IS NULL OR s.document_id = @DocumentId)
            ORDER BY kd.created_at DESC, s.sheet_index ASC;
            """;

        var sheets = await conn.QueryAsync<TabularSheet>(
            new CommandDefinition(sql, new { TeamId = teamId, DepartingUserId = departingUserId, DocumentId = documentId }, cancellationToken: ct));

        return sheets
            .Where(s => caller.CanReadResource(s.DepartmentId, s.TeamId, s.Visibility))
            .ToList();
    }

    public async Task<TabularQueryResultDto?> QueryTabularSheetAsync(
        SecurityContext caller,
        TabularQueryRequest request,
        CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        var sheet = await conn.QueryFirstOrDefaultAsync<TabularSheet>(new CommandDefinition(
            """
            SELECT s.*, kd.title AS document_title
            FROM tabular_sheets s
            JOIN knowledge_documents kd ON kd.id = s.document_id
            WHERE s.id = @SheetId;
            """, new { request.SheetId }, cancellationToken: ct));

        if (sheet is null) return null;
        if (!caller.CanReadResource(sheet.DepartmentId, sheet.TeamId, sheet.Visibility))
            throw new UnauthorizedAccessException("Access denied to this team's spreadsheet artifact.");

        var columns = JsonSerializer.Deserialize<List<TabularColumnSchema>>(sheet.ColumnsJson, JsonOpts) ?? [];
        var formulas = JsonSerializer.Deserialize<List<SpreadsheetFormulaInfo>>(sheet.FormulasJson, JsonOpts) ?? [];

        var allRows = (await conn.QueryAsync<TabularRow>(new CommandDefinition(
            "SELECT * FROM tabular_rows WHERE sheet_id = @SheetId ORDER BY row_index ASC;",
            new { request.SheetId }, cancellationToken: ct))).ToList();

        var parsedRows = allRows.Select(r =>
            JsonSerializer.Deserialize<Dictionary<string, object?>>(r.RowDataJson, JsonOpts) ?? new Dictionary<string, object?>())
            .ToList();

        IEnumerable<Dictionary<string, object?>> filtered = parsedRows;

        if (!string.IsNullOrWhiteSpace(request.FilterColumn) && !string.IsNullOrWhiteSpace(request.FilterValue))
        {
            var colMatch = columns.FirstOrDefault(c =>
                c.Name.Equals(request.FilterColumn, StringComparison.OrdinalIgnoreCase) ||
                c.NormalizedName.Equals(request.FilterColumn, StringComparison.OrdinalIgnoreCase))?.Name ?? request.FilterColumn;

            var op = (request.FilterOperator ?? "contains").ToLowerInvariant();
            filtered = filtered.Where(row =>
            {
                if (!row.TryGetValue(colMatch, out var rawVal) || rawVal is null) return false;
                var strVal = rawVal.ToString() ?? string.Empty;
                var filterNumeric = double.TryParse(request.FilterValue, out var fNum);
                var cellNumeric = double.TryParse(strVal.Replace("$", "").Replace(",", ""), out var cNum);

                return op switch
                {
                    "eq" or "=" => strVal.Equals(request.FilterValue, StringComparison.OrdinalIgnoreCase) || (filterNumeric && cellNumeric && Math.Abs(cNum - fNum) < 0.0001),
                    "gt" or ">" => filterNumeric && cellNumeric && cNum > fNum,
                    "gte" or ">=" => filterNumeric && cellNumeric && cNum >= fNum,
                    "lt" or "<" => filterNumeric && cellNumeric && cNum < fNum,
                    "lte" or "<=" => filterNumeric && cellNumeric && cNum <= fNum,
                    _ => strVal.Contains(request.FilterValue, StringComparison.OrdinalIgnoreCase)
                };
            });
        }

        var matchedList = filtered.ToList();
        Dictionary<string, object?>? aggSummary = null;

        if (!string.IsNullOrWhiteSpace(request.AggregateColumn) && !string.IsNullOrWhiteSpace(request.AggregateFunction))
        {
            var aggCol = columns.FirstOrDefault(c =>
                c.Name.Equals(request.AggregateColumn, StringComparison.OrdinalIgnoreCase) ||
                c.NormalizedName.Equals(request.AggregateColumn, StringComparison.OrdinalIgnoreCase))?.Name ?? request.AggregateColumn;

            var fn = request.AggregateFunction.ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(request.GroupByColumn))
            {
                var grpCol = columns.FirstOrDefault(c =>
                    c.Name.Equals(request.GroupByColumn, StringComparison.OrdinalIgnoreCase) ||
                    c.NormalizedName.Equals(request.GroupByColumn, StringComparison.OrdinalIgnoreCase))?.Name ?? request.GroupByColumn;

                var groups = matchedList
                    .GroupBy(r => r.TryGetValue(grpCol, out var gv) && gv is not null ? gv.ToString() ?? "(blank)" : "(blank)")
                    .ToDictionary(
                        g => g.Key,
                        g => (object?)ComputeAggregate(g, aggCol, fn));

                aggSummary = new Dictionary<string, object?>
                {
                    ["function"] = fn.ToUpperInvariant(),
                    ["column"] = aggCol,
                    ["groupBy"] = grpCol,
                    ["groups"] = groups
                };
            }
            else
            {
                aggSummary = new Dictionary<string, object?>
                {
                    ["function"] = fn.ToUpperInvariant(),
                    ["column"] = aggCol,
                    ["value"] = ComputeAggregate(matchedList, aggCol, fn),
                    ["matchedRows"] = matchedList.Count
                };
            }
        }

        var limit = Math.Clamp(request.Limit, 1, 200);

        return new TabularQueryResultDto(
            sheet.Id,
            sheet.DocumentId,
            sheet.DocumentTitle,
            sheet.SheetName,
            sheet.RowCount,
            matchedList.Count,
            columns,
            formulas,
            matchedList.Take(limit).ToList(),
            aggSummary);
    }

    private static double ComputeAggregate(IEnumerable<Dictionary<string, object?>> rows, string col, string fn)
    {
        if (fn == "count") return rows.Count();
        var nums = rows
            .Select(r => r.TryGetValue(col, out var v) && v is not null ? v.ToString() : null)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => double.TryParse(s!.Replace("$", "").Replace(",", ""), out var n) ? (double?)n : null)
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .ToList();

        if (nums.Count == 0) return 0;
        return fn switch
        {
            "sum" => Math.Round(nums.Sum(), 2),
            "avg" or "average" => Math.Round(nums.Average(), 2),
            "min" => Math.Round(nums.Min(), 2),
            "max" => Math.Round(nums.Max(), 2),
            _ => Math.Round(nums.Sum(), 2)
        };
    }

    // ==================== HANDOVER GAP QUESTIONS & SECRETS & AUDIT ====================

    public async Task SaveGapQuestionsAsync(IReadOnlyList<HandoverGapQuestion> gaps, CancellationToken ct = default)
    {
        if (gaps.Count == 0) return;
        await using var conn = CreateConnection();
        foreach (var g in gaps)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO handover_gap_questions (
                    id, departing_user_id, team_id, document_id, category, priority,
                    question, context_snippet, answer, answered_by_name, answered_at, status, indexed_chunk_id, created_at
                ) VALUES (
                    @Id, @DepartingUserId, @TeamId, @DocumentId, @Category, @Priority,
                    @Question, @ContextSnippet, @Answer, @AnsweredByName, @AnsweredAt, @StatusInt, @IndexedChunkId, @CreatedAt
                )
                ON CONFLICT (id) DO UPDATE SET
                    answer = EXCLUDED.answer,
                    answered_by_name = EXCLUDED.answered_by_name,
                    answered_at = EXCLUDED.answered_at,
                    status = EXCLUDED.status,
                    indexed_chunk_id = EXCLUDED.indexed_chunk_id;
                """,
                new
                {
                    g.Id,
                    g.DepartingUserId,
                    g.TeamId,
                    g.DocumentId,
                    g.Category,
                    g.Priority,
                    g.Question,
                    g.ContextSnippet,
                    g.Answer,
                    g.AnsweredByName,
                    g.AnsweredAt,
                    StatusInt = (int)g.Status,
                    g.IndexedChunkId,
                    g.CreatedAt
                }, cancellationToken: ct));
        }
    }

    public async Task<IReadOnlyList<HandoverGapQuestion>> GetGapQuestionsAsync(
        SecurityContext caller,
        Guid? teamId = null,
        Guid? departingUserId = null,
        CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        const string sql = """
            SELECT g.*, du.full_name AS departing_user_name, t.name AS team_name, kd.title AS document_title, t.department_id
            FROM handover_gap_questions g
            JOIN departing_users du ON du.id = g.departing_user_id
            JOIN teams t ON t.id = g.team_id
            LEFT JOIN knowledge_documents kd ON kd.id = g.document_id
            WHERE (@TeamId IS NULL OR g.team_id = @TeamId)
              AND (@DepartingUserId IS NULL OR g.departing_user_id = @DepartingUserId)
            ORDER BY g.status ASC, g.created_at DESC;
            """;

        var rows = await conn.QueryAsync<HandoverGapQuestion>(
            new CommandDefinition(sql, new { TeamId = teamId, DepartingUserId = departingUserId }, cancellationToken: ct));

        return rows
            .Where(g => caller.HasGlobalReadAccess || caller.AccessibleTeamIds.Contains(g.TeamId))
            .ToList();
    }

    public async Task<HandoverGapQuestion?> GetGapQuestionByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<HandoverGapQuestion>(new CommandDefinition(
            """
            SELECT g.*, du.full_name AS departing_user_name, t.name AS team_name, kd.title AS document_title
            FROM handover_gap_questions g
            JOIN departing_users du ON du.id = g.departing_user_id
            JOIN teams t ON t.id = g.team_id
            LEFT JOIN knowledge_documents kd ON kd.id = g.document_id
            WHERE g.id = @Id;
            """, new { Id = id }, cancellationToken: ct));
    }

    public async Task SaveSecretFindingsAsync(IReadOnlyList<SecretScanFinding> findings, CancellationToken ct = default)
    {
        if (findings.Count == 0) return;
        await using var conn = CreateConnection();
        foreach (var f in findings)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO secret_scan_findings (id, document_id, rule_name, severity, location, redacted_preview, is_acknowledged, created_at)
                VALUES (@Id, @DocumentId, @RuleName, @SeverityInt, @Location, @RedactedPreview, @IsAcknowledged, @CreatedAt);
                """,
                new
                {
                    f.Id,
                    f.DocumentId,
                    f.RuleName,
                    SeverityInt = (int)f.Severity,
                    f.Location,
                    f.RedactedPreview,
                    f.IsAcknowledged,
                    f.CreatedAt
                }, cancellationToken: ct));
        }
    }

    public async Task<IReadOnlyList<SecretScanFinding>> GetSecretFindingsAsync(Guid? documentId = null, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        var rows = await conn.QueryAsync<SecretScanFinding>(new CommandDefinition(
            """
            SELECT * FROM secret_scan_findings
            WHERE (@DocumentId IS NULL OR document_id = @DocumentId)
            ORDER BY is_acknowledged ASC, severity DESC, created_at DESC;
            """, new { DocumentId = documentId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task AcknowledgeSecretFindingAsync(Guid findingId, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE secret_scan_findings SET is_acknowledged = TRUE WHERE id = @Id;",
            new { Id = findingId }, cancellationToken: ct));
    }

    public async Task AppendAuditLogAsync(AuditLog log, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO audit_logs (id, timestamp, actor_user_id, actor_email, actor_role, action, resource_type, resource_id, team_id, details_json, ip_address)
            VALUES (@Id, @Timestamp, @ActorUserId, @ActorEmail, @ActorRole, @Action, @ResourceType, @ResourceId, @TeamId, @DetailsJson::jsonb, @IpAddress);
            """, log, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AuditLog>> GetAuditLogsAsync(int limit = 100, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        var rows = await conn.QueryAsync<AuditLog>(new CommandDefinition(
            "SELECT * FROM audit_logs ORDER BY timestamp DESC LIMIT @Limit;",
            new { Limit = Math.Clamp(limit, 1, 500) }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<int> RecalculateUserReadinessScoreAsync(Guid departingUserId, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        var docCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM knowledge_documents WHERE departing_user_id = @Id AND status = 2;",
            new { Id = departingUserId }, cancellationToken: ct));

        var totalGaps = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM handover_gap_questions WHERE departing_user_id = @Id;",
            new { Id = departingUserId }, cancellationToken: ct));

        var answeredGaps = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM handover_gap_questions WHERE departing_user_id = @Id AND status IN (1, 2);",
            new { Id = departingUserId }, cancellationToken: ct));

        // Up to 55 points from uploaded & processed artifacts (1st=25, 2nd=40, 3+=55) + up to 45 points from resolving handover gaps
        var docPoints = docCount switch
        {
            0 => 0,
            1 => 30,
            2 => 45,
            _ => 55
        };

        var gapPoints = totalGaps == 0
            ? (docCount > 0 ? 35 : 0)
            : (int)Math.Round(45.0 * answeredGaps / totalGaps);

        var score = Math.Clamp(docPoints + gapPoints, 0, 100);
        var newStatus = score switch
        {
            0 => HandoverStatus.NotStarted,
            >= 85 => HandoverStatus.ReadyForExit,
            >= 60 => HandoverStatus.PendingReview,
            _ => HandoverStatus.InProgress
        };

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE departing_users SET readiness_score = @Score, status = @StatusInt, updated_at = NOW() WHERE id = @Id;",
            new { Id = departingUserId, Score = score, StatusInt = (int)newStatus }, cancellationToken: ct));

        return score;
    }

    public async Task<DashboardMetricsDto> GetDashboardMetricsAsync(
        SecurityContext caller,
        string aiMode,
        CancellationToken ct = default)
    {
        var depts = await GetDepartmentsAsync(ct);
        var teams = await GetTeamsAsync(null, ct);
        var departingUsers = await GetDepartingUsersAsync(null, null, ct);
        var visibleDocs = await GetAccessibleDocumentsAsync(caller, ct: ct);
        var gaps = await GetGapQuestionsAsync(caller, ct: ct);

        await using var conn = CreateConnection();
        var totalChunks = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM document_chunks;", cancellationToken: ct));
        var totalSheets = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM tabular_sheets;", cancellationToken: ct));
        var unackSecrets = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM secret_scan_findings WHERE is_acknowledged = FALSE;", cancellationToken: ct));

        var scopedUsers = caller.HasGlobalReadAccess
            ? departingUsers
            : departingUsers.Where(u => caller.AccessibleTeamIds.Contains(u.TeamId)).ToList();

        var scopedTeams = caller.HasGlobalReadAccess
            ? teams
            : teams.Where(t => caller.AccessibleTeamIds.Contains(t.Id)).ToList();

        var decCount = scopedUsers.Count(u => u.ExitDate.Month == 12);
        var avgReadiness = scopedUsers.Count > 0 ? Math.Round(scopedUsers.Average(u => u.ReadinessScore), 1) : 0.0;

        var formatBreakdown = Enum.GetValues<ArtifactFormat>()
            .Select(f => new FormatCountDto(f.ToString(), visibleDocs.Count(d => d.Format == f)))
            .ToList();

        var teamReadiness = scopedTeams.Select(t =>
        {
            var tUsers = departingUsers.Where(u => u.TeamId == t.Id).ToList();
            var tDocs = visibleDocs.Where(d => d.TeamId == t.Id).ToList();
            var tOpenGaps = gaps.Count(g => g.TeamId == t.Id && g.Status == GapQuestionStatus.Open);
            var tAvg = tUsers.Count > 0 ? Math.Round(tUsers.Average(u => u.ReadinessScore), 1) : 0.0;
            return new TeamReadinessSummaryDto(t.Id, t.Name, t.DepartmentName, tUsers.Count, tDocs.Count, tOpenGaps, tAvg);
        }).ToList();

        return new DashboardMetricsDto(
            depts.Count,
            scopedTeams.Count,
            scopedUsers.Count,
            decCount,
            avgReadiness,
            visibleDocs.Count,
            totalChunks,
            totalSheets,
            gaps.Count(g => g.Status == GapQuestionStatus.Open),
            gaps.Count(g => g.Status == GapQuestionStatus.Answered),
            unackSecrets,
            aiMode,
            IsPgVectorEnabled,
            formatBreakdown,
            teamReadiness,
            scopedUsers.OrderBy(u => u.ExitDate).Take(10).ToList());
    }

    private sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override DateOnly Parse(object value) => value switch
        {
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => DateOnly.Parse(value.ToString()!)
        };

        public override void SetValue(System.Data.IDbDataParameter parameter, DateOnly value)
        {
            parameter.Value = value.ToDateTime(TimeOnly.MinValue);
        }
    }
}
