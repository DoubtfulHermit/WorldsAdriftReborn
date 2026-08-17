namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Guards the retail relative-parent recovery hack from toggling a hierarchy
    /// behaviour before SpatialOS has injected its TransformState reader, or
    /// while the entity is being removed and that reader has already gone away.
    /// Toggling in either window makes the retail OnEnable/OnDisable methods
    /// dereference null and can turn ordinary interest unload into an exception
    /// storm on the Unity main thread.
    /// </summary>
    public static class HierarchyLifecyclePolicy
    {
        public static bool MayRunInjectedLifecycle(
            bool behaviourPresent,
            bool transformStateReaderPresent,
            bool gameObjectActive)
        {
            return behaviourPresent && transformStateReaderPresent && gameObjectActive;
        }
    }
}
