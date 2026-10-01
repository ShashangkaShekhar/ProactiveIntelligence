internal static class SqlQueryValidator
{
    public static bool IsSelectStatement(string? sql)
    {
        var trimmedSql = sql?.TrimStart();
        if (string.IsNullOrEmpty(trimmedSql) || !trimmedSql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return trimmedSql.Length == "SELECT".Length
            || !IsIdentifierCharacter(trimmedSql["SELECT".Length]);
    }

    private static bool IsIdentifierCharacter(char character) =>
        char.IsLetterOrDigit(character) || character == '_';
}