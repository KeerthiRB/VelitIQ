import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Horizon VelitIQ — Banking Knowledge Continuity & Agentic MCP Platform",
  description:
    "Categorized Team & User Knowledge Capture, Hybrid RAG, Spreadsheet Formula SQL, and MCP Server Suite (.NET 10 + PostgreSQL).",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en" className="dark">
      <body className="bg-slate-950 text-slate-100 antialiased min-h-screen">
        {children}
      </body>
    </html>
  );
}
