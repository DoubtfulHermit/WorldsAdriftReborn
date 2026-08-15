using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;

namespace WorldsAdriftRebornGameServer.Multiplayer.Domains
{
    public enum SimulationDomainKind
    {
        Island,
        Ship,
    }

    /// <summary>
    /// Ownership-only local domain contract. It intentionally has no Tick method:
    /// Phase 4A must not reorder the proven single poll loop or pretend an empty
    /// island shell already owns simulation behavior.
    /// </summary>
    public interface ILocalSimulationDomain
    {
        SimulationDomainId Id { get; }
        SimulationDomainKind Kind { get; }
        IReadOnlyList<long> EntityIds { get; }
    }
}
