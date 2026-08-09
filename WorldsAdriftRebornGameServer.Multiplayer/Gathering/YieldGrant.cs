namespace WorldsAdriftRebornGameServer.Multiplayer.Gathering
{
    /// <summary>
    /// A resolved harvest yield: the exact item, count and quality one hit
    /// produced, ready to hand to the inventory and to the salvage-feedback toast.
    ///
    /// It is the output of <see cref="HarvestYield.Resolve"/> and the input to
    /// two game-side steps that MUST agree, or the panel and the toast disagree:
    /// the 1081 grant uses <see cref="Amount"/> for the stack, and the 8060
    /// FeedbackListener event uses the SAME <see cref="Amount"/> for "Salvaged
    /// Iron x<Amount>". Carrying one value through both is what keeps them equal.
    /// </summary>
    public readonly record struct YieldGrant(string ItemTypeId, int Amount, int Quality);
}
