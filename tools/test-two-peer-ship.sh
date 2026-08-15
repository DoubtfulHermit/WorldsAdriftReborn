#!/usr/bin/env bash
# Deterministic server-side acceptance gate for the Colin multiplayer ship path.
# This does not launch Unity and cannot approve camera/IK/rendering. It does run
# the complete two-peer authority/replication/aboard/checkout journey and then
# compile the real game-server integration that consumes those policies.
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"

dotnet test "$repo/WorldsAdriftRebornGameServer.Multiplayer.Tests" -c Release \
  --filter 'FullyQualifiedName~TwoPeerShipAcceptanceTests|FullyQualifiedName~ShipDomainTests|FullyQualifiedName~ShipReplicationCursorTests|FullyQualifiedName~AboardRelayPolicyTests|FullyQualifiedName~ShipDomainInterestPolicyTests|FullyQualifiedName~ShipConnectLifecycleTests|FullyQualifiedName~FlightSessionTests|FullyQualifiedName~ShipPartMotionPolicyTests'

dotnet build "$repo/WorldsAdriftRebornGameServer" -c Release

echo "[ship-acceptance] PASS: deterministic two-peer ship journey and server build."
echo "[ship-acceptance] This is tier 1; tier 2 ENet helm-driving relaybot is still required before wire acceptance."
