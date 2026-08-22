namespace WorldsAdriftRebornGameServer.Multiplayer
{
    public enum AdminWorldCommandKind
    {
        ResetResources,
        RecallShip,
        StopShip,
        ReleaseHelm,
        DeleteShip,
        StageShip,
        ReleaseStagedShip,
    }

    public readonly record struct AdminWorldCommand(
        AdminWorldCommandKind Kind, long HullEntityId, long PlayerEntityId,
        double X = 0, double Y = 0, double Z = 0);

    /// <summary>Strict parser for the authenticated web console's one-shot bridge.</summary>
    public static class AdminWorldCommandPolicy
    {
        public static bool TryParse(string? line, out AdminWorldCommand command,
            out string error)
        {
            command = default;
            error = string.Empty;
            string[] fields = (line ?? string.Empty).Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 2 && fields[0] == "reset-resources"
                && fields[1] == "all")
            {
                command = new AdminWorldCommand(AdminWorldCommandKind.ResetResources, 0, 0);
                return true;
            }
            if (fields.Length == 3 && fields[0] == "recall-ship"
                && Positive(fields[1], out long hull) && Positive(fields[2], out long player))
            {
                command = new AdminWorldCommand(AdminWorldCommandKind.RecallShip, hull, player);
                return true;
            }
            if (fields.Length == 2 && fields[0] == "stop-ship"
                && Positive(fields[1], out hull))
            {
                command = new AdminWorldCommand(AdminWorldCommandKind.StopShip, hull, 0);
                return true;
            }
            if (fields.Length == 2 && fields[0] == "release-helm"
                && Positive(fields[1], out hull))
            {
                command = new AdminWorldCommand(AdminWorldCommandKind.ReleaseHelm, hull, 0);
                return true;
            }
            if (fields.Length == 3 && fields[0] == "delete-ship"
                && Positive(fields[1], out hull) && fields[2] == "DELETE")
            {
                command = new AdminWorldCommand(AdminWorldCommandKind.DeleteShip, hull, 0);
                return true;
            }
            if (fields.Length == 5 && fields[0] == "stage-ship"
                && Positive(fields[1], out hull)
                && Coordinate(fields[2], -18050, 18050, out double x)
                && Coordinate(fields[3], -20000, 1100, out double y)
                && Coordinate(fields[4], -18050, 18050, out double z))
            {
                command = new AdminWorldCommand(AdminWorldCommandKind.StageShip, hull, 0,
                    x, y, z);
                return true;
            }
            if (fields.Length == 2 && fields[0] == "release-staged-ship"
                && Positive(fields[1], out hull))
            {
                command = new AdminWorldCommand(AdminWorldCommandKind.ReleaseStagedShip, hull, 0);
                return true;
            }
            error = "expected reset-resources all, recall-ship <hull> <player>, stop-ship <hull>, release-helm <hull>, delete-ship <hull> DELETE, stage-ship <hull> <x> <y> <z>, or release-staged-ship <hull>";
            return false;
        }

        private static bool Positive(string value, out long parsed) =>
            long.TryParse(value, out parsed) && parsed > 0;

        private static bool Coordinate(string value, double minimum, double maximum,
            out double parsed) =>
            double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out parsed)
            && double.IsFinite(parsed) && parsed >= minimum && parsed <= maximum;
    }

    /// <summary>
    /// Pure safety rule for the offline boundary-test harness. The character's
    /// durable logout position must already be close to the hull; then the exact
    /// world-space offset is carried to the staged pose. This prevents an operator
    /// typo from relocating an unrelated owner's saved character.
    /// </summary>
    public static class AdminShipStagePolicy
    {
        public const double MaximumOwnerDistanceMetres = 40.0;

        public static bool TryCarryLogoutPosition(FixedPointPosition hull,
            FixedPointPosition storedPlayer, FixedPointPosition destination,
            out FixedPointPosition carried)
        {
            double dx = storedPlayer.MetresX - hull.MetresX;
            double dy = storedPlayer.MetresY - hull.MetresY;
            double dz = storedPlayer.MetresZ - hull.MetresZ;
            double distanceSquared = dx * dx + dy * dy + dz * dz;
            if (!double.IsFinite(distanceSquared)
                || distanceSquared > MaximumOwnerDistanceMetres * MaximumOwnerDistanceMetres)
            {
                carried = default;
                return false;
            }

            carried = FixedPointPosition.FromMetres(
                destination.MetresX + dx,
                destination.MetresY + dy,
                destination.MetresZ + dz);
            return true;
        }
    }
}
