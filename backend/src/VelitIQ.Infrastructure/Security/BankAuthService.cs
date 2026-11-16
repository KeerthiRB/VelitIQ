using System.DirectoryServices.Protocols;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VelitIQ.Core.Configuration;
using VelitIQ.Core.DTOs;
using VelitIQ.Core.Interfaces;
using VelitIQ.Core.Models;
using VelitIQ.Infrastructure.Data;

namespace VelitIQ.Infrastructure.Security;

public static class PasswordHasher
{
    public static string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash)) return false;
        var parts = storedHash.Split('.');
        if (parts.Length != 2) return false;
        var salt = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

public sealed class BankAuthService : IAuthService
{
    private readonly PostgresKnowledgeStore _store;
    private readonly AuthOptions _options;
    private readonly ILogger<BankAuthService> _logger;

    public BankAuthService(
        PostgresKnowledgeStore store,
        IOptions<AuthOptions> options,
        ILogger<BankAuthService> logger)
    {
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AuthTokenResponse?> LoginWithCredentialsAsync(LoginRequest request, string ipAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.UsernameOrEmail) || string.IsNullOrWhiteSpace(request.Password))
            return null;

        // 1. If On-Prem Active Directory LDAP is enabled, attempt LDAP Bind + AD Security Group synchronization first
        if (_options.Ldap.Enabled)
        {
            var adUser = await TryAuthenticateViaLdapAsync(request, ct);
            if (adUser is not null)
            {
                await _store.UpdateLastLoginAsync(adUser.Id, ct);
                await LogAuthEventAsync(adUser, "AUTH_LOGIN_AD_LDAP", ipAddress, ct);
                return await IssueJwtAsync(adUser, ct);
            }

            if (_options.Mode.Equals("ActiveDirectoryOnly", StringComparison.OrdinalIgnoreCase))
                return null;
        }

        // 2. Local / Bootstrap Bank Account Verification (used locally & for emergency break-glass Admin)
        var user = await _store.GetUserByUsernameOrEmailAsync(request.UsernameOrEmail.Trim(), ct);
        if (user is null || !user.IsActive || !PasswordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            await _store.AppendAuditLogAsync(new AuditLog
            {
                ActorEmail = request.UsernameOrEmail,
                Action = "AUTH_LOGIN_FAILED",
                ResourceType = "Auth",
                DetailsJson = "{\"reason\":\"Invalid credentials\"}",
                IpAddress = ipAddress
            }, ct);
            return null;
        }

        await _store.UpdateLastLoginAsync(user.Id, ct);
        await LogAuthEventAsync(user, "AUTH_LOGIN_LOCAL", ipAddress, ct);
        return await IssueJwtAsync(user, ct);
    }

    public async Task<AuthTokenResponse?> LoginDemoPersonaAsync(string email, string ipAddress, CancellationToken ct = default)
    {
        if (!_options.EnableDemoPersonaSelector)
            return null;

        var user = await _store.GetUserByUsernameOrEmailAsync(email.Trim(), ct);
        if (user is null || !user.IsActive)
            return null;

        await _store.UpdateLastLoginAsync(user.Id, ct);
        await LogAuthEventAsync(user, "AUTH_LOGIN_DEMO_PERSONA", ipAddress, ct);
        return await IssueJwtAsync(user, ct);
    }

    public async Task<AuthTokenResponse?> LoginWindowsIdentityAsync(
        string windowsAccountName,
        IEnumerable<string> groupSidsOrNames,
        string ipAddress,
        CancellationToken ct = default)
    {
        // Normalize DOMAIN\username -> username
        var shortUser = windowsAccountName.Contains('\\')
            ? windowsAccountName.Split('\\', 2)[1]
            : windowsAccountName;

        var groups = groupSidsOrNames.ToList();
        var teams = await _store.GetTeamsAsync(null, ct);
        var matchedTeams = teams
            .Where(t => !string.IsNullOrWhiteSpace(t.AdGroupName) &&
                        groups.Any(g => g.Contains(t.AdGroupName, StringComparison.OrdinalIgnoreCase)))
            .Select(t => t.Id)
            .ToArray();

        var role = UserRole.Employee;
        if (!string.IsNullOrWhiteSpace(_options.Ldap.AdminGroupCn) &&
            groups.Any(g => g.Contains(_options.Ldap.AdminGroupCn, StringComparison.OrdinalIgnoreCase)))
        {
            role = UserRole.Admin;
        }
        else if (!string.IsNullOrWhiteSpace(_options.Ldap.CrossTeamGroupCn) &&
                 groups.Any(g => g.Contains(_options.Ldap.CrossTeamGroupCn, StringComparison.OrdinalIgnoreCase)))
        {
            role = UserRole.CrossTeamViewer;
        }

        var existing = await _store.GetUserByUsernameOrEmailAsync(shortUser, ct);
        var appUser = existing ?? new AppUser
        {
            Username = shortUser,
            Email = $"{shortUser.ToLowerInvariant()}@horizonbank.com",
            DisplayName = shortUser,
            JobTitle = "AD Domain User",
            AuthSource = "ActiveDirectory",
            Role = role,
            PrimaryTeamId = matchedTeams.FirstOrDefault(),
            AdditionalTeamIds = matchedTeams.Skip(1).ToArray()
        };

        if (existing is not null && matchedTeams.Length > 0)
        {
            appUser.PrimaryTeamId ??= matchedTeams[0];
            appUser.AdditionalTeamIds = appUser.AdditionalTeamIds.Union(matchedTeams).Distinct().ToArray();
        }

        await _store.UpsertAppUserAsync(appUser, ct);
        await LogAuthEventAsync(appUser, "AUTH_LOGIN_WINDOWS_SSO", ipAddress, ct);
        return await IssueJwtAsync(appUser, ct);
    }

    private async Task<AppUser?> TryAuthenticateViaLdapAsync(LoginRequest request, CancellationToken ct)
    {
        try
        {
            var ldapCfg = _options.Ldap;
            var domain = string.IsNullOrWhiteSpace(request.Domain) ? ldapCfg.Domain : request.Domain;
            var rawUser = request.UsernameOrEmail.Contains('\\')
                ? request.UsernameOrEmail.Split('\\', 2)[1]
                : request.UsernameOrEmail.Split('@')[0];

            var identifier = new LdapDirectoryIdentifier(ldapCfg.Host, ldapCfg.Port);
            using var connection = new LdapConnection(identifier)
            {
                AuthType = AuthType.Negotiate
            };
            connection.SessionOptions.ProtocolVersion = 3;
            if (ldapCfg.UseSsl) connection.SessionOptions.SecureSocketLayer = true;

            var networkCred = new NetworkCredential(rawUser, request.Password, domain);
            connection.Bind(networkCred);

            // Search user attributes & memberOf AD groups
            var filter = $"(&(objectClass=user)(|(sAMAccountName={EscapeLdapFilter(rawUser)})(userPrincipalName={EscapeLdapFilter(request.UsernameOrEmail)})))";
            var searchReq = new SearchRequest(ldapCfg.BaseDn, filter, SearchScope.Subtree, "displayName", "mail", "title", "memberOf");
            var searchResp = (SearchResponse)connection.SendRequest(searchReq);

            string displayName = rawUser;
            string email = $"{rawUser.ToLowerInvariant()}@horizonbank.com";
            string title = "Bank Staff";
            var memberOfGroups = new List<string>();

            if (searchResp.Entries.Count > 0)
            {
                var entry = searchResp.Entries[0];
                if (entry.Attributes["displayName"]?.Count > 0) displayName = entry.Attributes["displayName"][0]?.ToString() ?? displayName;
                if (entry.Attributes["mail"]?.Count > 0) email = entry.Attributes["mail"][0]?.ToString() ?? email;
                if (entry.Attributes["title"]?.Count > 0) title = entry.Attributes["title"][0]?.ToString() ?? title;
                if (entry.Attributes["memberOf"] is not null)
                {
                    foreach (var g in entry.Attributes["memberOf"])
                    {
                        if (g is not null) memberOfGroups.Add(g.ToString()!);
                    }
                }
            }

            var allTeams = await _store.GetTeamsAsync(null, ct);
            var matchedTeams = allTeams
                .Where(t => !string.IsNullOrWhiteSpace(t.AdGroupName) &&
                            memberOfGroups.Any(dn => dn.Contains(t.AdGroupName, StringComparison.OrdinalIgnoreCase)))
                .Select(t => t.Id)
                .ToArray();

            var role = UserRole.Employee;
            if (memberOfGroups.Any(dn => dn.Contains(ldapCfg.AdminGroupCn, StringComparison.OrdinalIgnoreCase)))
                role = UserRole.Admin;
            else if (memberOfGroups.Any(dn => dn.Contains(ldapCfg.CrossTeamGroupCn, StringComparison.OrdinalIgnoreCase)))
                role = UserRole.CrossTeamViewer;

            var existing = await _store.GetUserByUsernameOrEmailAsync(email, ct) ?? await _store.GetUserByUsernameOrEmailAsync(rawUser, ct);
            var synced = new AppUser
            {
                Id = existing?.Id ?? Guid.NewGuid(),
                Username = rawUser,
                Email = email,
                DisplayName = displayName,
                JobTitle = title,
                AuthSource = "ActiveDirectory",
                Role = existing?.Role == UserRole.Admin ? UserRole.Admin : role,
                PrimaryTeamId = matchedTeams.FirstOrDefault() != Guid.Empty ? matchedTeams.First() : existing?.PrimaryTeamId,
                AdditionalTeamIds = matchedTeams.Skip(1).ToArray(),
                IsActive = true,
                LastLoginAt = DateTimeOffset.UtcNow
            };

            await _store.UpsertAppUserAsync(synced, ct);
            return synced;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("LDAP bind failed for '{User}': {Message}", request.UsernameOrEmail, ex.Message);
            return null;
        }
    }

    private static string EscapeLdapFilter(string input) =>
        input.Replace("\\", "\\5c").Replace("*", "\\2a").Replace("(", "\\28").Replace(")", "\\29").Replace("\0", "\\00");

    public async Task<IReadOnlyList<DemoPersonaDto>> GetDemoPersonasAsync(CancellationToken ct = default)
    {
        if (!_options.EnableDemoPersonaSelector) return [];

        var users = await _store.GetAllAppUsersAsync(ct);
        var teams = (await _store.GetTeamsAsync(null, ct)).ToDictionary(t => t.Id);

        return users.Select(u =>
        {
            var team = u.PrimaryTeamId.HasValue && teams.TryGetValue(u.PrimaryTeamId.Value, out var t) ? t : null;
            var highlight = u.Role switch
            {
                UserRole.Admin => "Global Admin — Full Read/Write across ALL Departments, Teams, Audit Logs & AD Mappings",
                UserRole.CrossTeamViewer => "Audit & Risk Cross-Team Lead — Read/RAG access across ALL Departments & Teams",
                UserRole.TeamLead => $"Team Lead ({team?.Name ?? "Team"}) — Sees own Team's private docs + Shared Cross-Team docs",
                _ => $"Departing Staff ({team?.Name ?? "Team"}) — Uploads artifacts, completes AI Gap Q&A, queries authorized docs"
            };

            return new DemoPersonaDto(
                u.Email,
                u.DisplayName,
                u.JobTitle,
                u.Role,
                team?.Name ?? "Enterprise Governance (All Teams)",
                team?.DepartmentName ?? "Enterprise",
                highlight);
        }).ToList();
    }

    public async Task<SecurityContext> BuildSecurityContextAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _store.GetUserByIdAsync(userId, ct);
        if (user is null)
            return new SecurityContext { UserId = Guid.Empty, Role = UserRole.Employee };

        var allTeams = await _store.GetTeamsAsync(null, ct);
        var teamIds = new HashSet<Guid>();
        if (user.PrimaryTeamId.HasValue) teamIds.Add(user.PrimaryTeamId.Value);
        foreach (var tid in user.AdditionalTeamIds) teamIds.Add(tid);

        var deptIds = allTeams
            .Where(t => teamIds.Contains(t.Id))
            .Select(t => t.DepartmentId)
            .Distinct()
            .ToArray();

        Guid? primaryDeptId = user.PrimaryTeamId.HasValue
            ? allTeams.FirstOrDefault(t => t.Id == user.PrimaryTeamId.Value)?.DepartmentId
            : null;

        return new SecurityContext
        {
            UserId = user.Id,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role,
            PrimaryDepartmentId = primaryDeptId,
            AccessibleTeamIds = teamIds.ToArray(),
            AccessibleDepartmentIds = deptIds
        };
    }

    private async Task<AuthTokenResponse> IssueJwtAsync(AppUser user, CancellationToken ct)
    {
        var allTeams = await _store.GetTeamsAsync(null, ct);
        var primaryTeam = user.PrimaryTeamId.HasValue
            ? allTeams.FirstOrDefault(t => t.Id == user.PrimaryTeamId.Value)
            : null;

        var accessibleTeams = new List<Guid>();
        if (user.PrimaryTeamId.HasValue) accessibleTeams.Add(user.PrimaryTeamId.Value);
        accessibleTeams.AddRange(user.AdditionalTeamIds);
        var distinctTeams = accessibleTeams.Distinct().ToArray();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new("display_name", user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("auth_source", user.AuthSource)
        };

        foreach (var tid in distinctTeams)
        {
            claims.Add(new Claim("team_id", tid.ToString()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTimeOffset.UtcNow.AddHours(_options.TokenExpiryHours);

        var jwt = new JwtSecurityToken(
            issuer: _options.JwtIssuer,
            audience: _options.JwtAudience,
            claims: claims,
            expires: expires.UtcDateTime,
            signingCredentials: creds);

        var tokenStr = new JwtSecurityTokenHandler().WriteToken(jwt);

        var profileDto = new UserProfileDto(
            user.Id,
            user.Username,
            user.Email,
            user.DisplayName,
            user.JobTitle,
            user.AuthSource,
            user.Role,
            primaryTeam?.Id,
            primaryTeam?.Name,
            primaryTeam?.DepartmentId,
            primaryTeam?.DepartmentName,
            distinctTeams);

        return new AuthTokenResponse(tokenStr, expires, profileDto);
    }

    private Task LogAuthEventAsync(AppUser user, string action, string ip, CancellationToken ct) =>
        _store.AppendAuditLogAsync(new AuditLog
        {
            ActorUserId = user.Id,
            ActorEmail = user.Email,
            ActorRole = user.Role.ToString(),
            Action = action,
            ResourceType = "AppUser",
            ResourceId = user.Id.ToString(),
            TeamId = user.PrimaryTeamId,
            DetailsJson = $"{{\"authSource\":\"{user.AuthSource}\"}}",
            IpAddress = ip
        }, ct);
}
