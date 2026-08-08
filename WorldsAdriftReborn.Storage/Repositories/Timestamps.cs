namespace WorldsAdriftReborn.Storage.Repositories
{
    /// <summary>
    /// How instants cross the ADO boundary.
    ///
    /// The columns are TIMESTAMPTZ and Npgsql's canonical mapping for that is a
    /// DateTime with Kind=Utc, so every value is normalised to UTC on the way in
    /// and re-wrapped on the way out. Records expose DateTimeOffset because a
    /// caller that has to remember a DateTime is UTC eventually forgets.
    /// </summary>
    internal static class Timestamps
    {
        internal static DateTime ToDb(DateTimeOffset value)
        {
            return value.UtcDateTime;
        }

        internal static object ToDb(DateTimeOffset? value)
        {
            return value.HasValue ? ToDb(value.Value) : DBNull.Value;
        }

        internal static DateTimeOffset FromDb(DateTime value)
        {
            return new DateTimeOffset(
                DateTime.SpecifyKind(value, DateTimeKind.Utc));
        }
    }
}
