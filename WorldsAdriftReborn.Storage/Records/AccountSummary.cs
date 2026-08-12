namespace WorldsAdriftReborn.Storage.Records
{
    /// <summary>
    /// A read-only projection of an account for the operator dashboard: what a
    /// human wants to see about a signup, and NOTHING a human should never see.
    ///
    /// Deliberately NOT <see cref="AccountRecord"/>: that carries the password
    /// hash and the steam key, and the dashboard has no business rendering
    /// either. Selecting into a narrower type is what makes "the panel cannot
    /// leak a hash" a property of the query shape rather than of every call site
    /// remembering not to print a field.
    /// </summary>
    /// <param name="Username">The form the player typed, for display.</param>
    /// <param name="CreatedAt">When the account was created.</param>
    /// <param name="CharacterCount">
    /// How many real (non-empty-slot) characters the account owns.
    /// </param>
    public sealed record AccountSummary(
        string Username,
        DateTimeOffset CreatedAt,
        int CharacterCount);
}
