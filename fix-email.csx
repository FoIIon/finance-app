#!/usr/bin/env dotnet-script
#r "nuget: Microsoft.Data.Sqlite, 8.0.26"

using Microsoft.Data.Sqlite;

var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "backend", "FinanceApp.API", "finance.db");
dbPath = Path.GetFullPath(dbPath);

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

var cmd = conn.CreateCommand();
cmd.CommandText = "UPDATE Users SET EmailConfirmed = 1, EmailConfirmationToken = NULL, EmailConfirmationTokenExpiry = NULL";
var rows = cmd.ExecuteNonQuery();
Console.WriteLine($"{rows} utilisateur(s) confirmé(s)");

var listCmd = conn.CreateCommand();
listCmd.CommandText = "SELECT Id, Email, EmailConfirmed FROM Users";
using var reader = listCmd.ExecuteReader();
while (reader.Read())
    Console.WriteLine($"  [{reader.GetInt64(0)}] {reader.GetString(1)} — confirmé: {reader.GetBoolean(2)}");
