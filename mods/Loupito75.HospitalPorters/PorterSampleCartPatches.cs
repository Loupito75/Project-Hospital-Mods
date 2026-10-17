using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPorters
{
    internal static class PorterSampleCartRuntime
    {
        internal static bool HasFreeCart(Entity porter)
        {
            return FindFreeCart(porter, out Room _) != null;
        }

        internal static TileObject GetFreeCart(Entity porter)
        {
            Room homeRoom;
            return FindFreeCart(porter, out homeRoom);
        }

        private static void SetCartDepartment(
            TileObject cart,
            Department department)
        {
            if (cart == null || department == null)
            {
                return;
            }

            Department currentDepartment =
                cart.m_state.m_department == null
                    ? null
                    : cart.m_state.m_department.GetEntity();
            if (currentDepartment != department)
            {
                if (currentDepartment != null)
                {
                    currentDepartment.RemoveObject(cart);
                }

                cart.m_state.m_department =
                    department;
            }

            if (!department.m_departmentPersistentData.m_objects.Contains(
                    cart))
            {
                department.AddObject(cart);
            }
        }

        internal static void SyncCartDepartmentForState(
            TileObject cart,
            Room homeRoom)
        {
            if (cart == null ||
                MapScriptInterface.Instance == null)
            {
                return;
            }

            Department targetDepartment = null;
            if (!cart.m_state.m_moving &&
                !cart.IsAttachedToCharacter())
            {
                Room physicalRoom =
                    MapScriptInterface.Instance.GetRoomAt(
                        cart.m_state.m_position,
                        cart.GetFloorIndex());
                targetDepartment =
                    physicalRoom == null ||
                    physicalRoom.m_roomPersistentData.m_department == null
                        ? null
                        : physicalRoom.m_roomPersistentData.m_department.GetEntity();
            }

            if (targetDepartment == null)
            {
                targetDepartment =
                    homeRoom == null ||
                    homeRoom.m_roomPersistentData.m_department == null
                        ? null
                        : homeRoom.m_roomPersistentData.m_department.GetEntity();
            }

            SetCartDepartment(
                cart,
                targetDepartment);
        }

        internal static bool TryReserveCart(
            Entity porter,
            out TileObject cart,
            out Room homeRoom)
        {
            cart = FindFreeCart(porter, out homeRoom);
            if (cart == null || cart.User != null || cart.Owner != null)
            {
                cart = null;
                homeRoom = null;
                return false;
            }

            cart.User = porter;
            if (cart.User != porter)
            {
                cart = null;
                homeRoom = null;
                return false;
            }

            return true;
        }

        internal static void ReleaseReservedCart(
            TileObject cart,
            Entity porter)
        {
            if (cart == null)
            {
                return;
            }

            if (!cart.m_state.m_moving && cart.User == porter)
            {
                cart.User = null;
                cart.m_state.m_originalFloorIndex = -1;
            }
        }

        internal static bool TryAttachCart(
            Entity porter,
            TileObject cart,
            Room homeRoom)
        {
            WalkComponent walk =
                porter == null
                    ? null
                    : porter.GetComponent<WalkComponent>();
            Vector2i expectedTile =
                cart == null
                    ? Vector2i.ZERO_VECTOR
                    : new Vector2i(
                        (int)(cart.GetDefaultUsePosition().m_x + 0.5f),
                        (int)(cart.GetDefaultUsePosition().m_y + 0.5f));

            if (porter == null ||
                cart == null ||
                walk == null ||
                cart.User != porter ||
                cart.m_state.m_moving ||
                MapScriptInterface.Instance == null ||
                walk.GetFloorIndex() != cart.GetFloorIndex() ||
                walk.GetCurrentTile() != expectedTile)
            {
                PorterDiagnostics.Log(
                    "cart attach rejected: porter=" +
                    (porter == null
                        ? "UNKNOWN"
                        : (porter.Name ?? string.Empty).Trim()) +
                    "; porterTile=" +
                    (walk == null
                        ? "NONE"
                        : walk.GetCurrentTile().ToString()) +
                    "; porterFloor=" +
                    (walk == null
                        ? -1
                        : walk.GetFloorIndex()) +
                    "; expectedTile=" +
                    expectedTile +
                    "; cartPosition=" +
                    (cart == null
                        ? "NONE"
                        : cart.m_state.m_position.ToString()) +
                    "; cartFloor=" +
                    (cart == null
                        ? -1
                        : cart.GetFloorIndex()) +
                    "; moving=" +
                    (cart != null && cart.m_state.m_moving) +
                    "; userMatches=" +
                    (cart != null && cart.User == porter) +
                    ".");
                return false;
            }

            if (!MapScriptInterface.Instance.PickUpObject(cart))
            {
                PorterDiagnostics.Log(
                    "cart attach pickup failed: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; cartPosition=" +
                    cart.m_state.m_position +
                    "; cartFloor=" +
                    cart.GetFloorIndex() +
                    ".");
                return false;
            }

            Department homeDepartment =
                homeRoom == null ||
                homeRoom.m_roomPersistentData.m_department == null
                    ? null
                    : homeRoom.m_roomPersistentData.m_department.GetEntity();
            SetCartDepartment(
                cart,
                homeDepartment);

            cart.SetAttachedToCharacter(attached: true);

            bool attached =
                cart.m_state.m_moving &&
                cart.IsAttachedToCharacter() &&
                cart.User == porter;

            PorterDiagnostics.Log(
                "cart attach: porter=" +
                (porter.Name ?? string.Empty).Trim() +
                "; success=" +
                attached +
                "; cartPosition=" +
                cart.m_state.m_position +
                "; cartFloor=" +
                cart.GetFloorIndex() +
                ".");
            return attached;
        }

        internal static bool TryGetInteractionParkingTile(
            Entity porter,
            PorterSampleTransportJob job,
            TileObject interactionObject,
            out Vector2i parkingTile)
        {
            parkingTile = Vector2i.ZERO_VECTOR;
            TileObject cart =
                job == null
                    ? null
                    : job.Cart;
            WalkComponent walk =
                porter == null
                    ? null
                    : porter.GetComponent<WalkComponent>();
            if (porter == null ||
                cart == null ||
                walk == null ||
                interactionObject == null ||
                cart.User != porter ||
                MapScriptInterface.Instance == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
            {
                return false;
            }

            int floorIndex =
                interactionObject.GetFloorIndex();
            if (floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Room interactionRoom =
                MapScriptInterface.Instance.GetRoomAt(
                    interactionObject.m_state.m_position,
                    floorIndex);
            if (interactionRoom == null)
            {
                return false;
            }

            PorterSampleTransportRuntime.ClearCartParkingTarget(
                job);

            // Prefer a physically safe combined Porter + cart footprint.
            // Door proximity is a penalty, never a hard ban; a wall is
            // preferred only when the attached cart can remain on the same
            // side of that wall as the Porter.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!TryFindPreferredInteractionParkingTile(
                        porter,
                        cart,
                        interactionObject,
                        interactionRoom,
                        out parkingTile))
                {
                    break;
                }

                if (PorterSampleTransportRuntime.TryReserveCartParkingTarget(
                        porter,
                        job,
                        parkingTile,
                        floorIndex))
                {
                    return true;
                }
            }

            // Keep vanilla's conservative 3x3 search as a last fallback.
            // Revalidate the combined footprint so the fallback cannot place
            // the attached cart through a wall or on an occupied neighboring
            // tile.
            parkingTile =
                MapScriptInterface.Instance.FindClosest3x3Area(
                    interactionObject.m_state.m_position,
                    interactionRoom);
            if (parkingTile != Vector2i.ZERO_VECTOR &&
                IsInteractionParkingTileSafe(
                    porter,
                    cart,
                    interactionRoom,
                    parkingTile,
                    requireRoute: true) &&
                PorterSampleTransportRuntime.TryReserveCartParkingTarget(
                    porter,
                    job,
                    parkingTile,
                    floorIndex))
            {
                return true;
            }

            parkingTile = Vector2i.ZERO_VECTOR;
            return false;
        }

        internal static bool TryParkCartAtCurrentTile(
            Entity porter,
            PorterSampleTransportJob job)
        {
            TileObject cart =
                job == null
                    ? null
                    : job.Cart;
            WalkComponent walk =
                porter == null
                    ? null
                    : porter.GetComponent<WalkComponent>();
            if (porter == null ||
                cart == null ||
                walk == null ||
                cart.User != porter ||
                !cart.m_state.m_moving ||
                !cart.IsAttachedToCharacter() ||
                MapScriptInterface.Instance == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null ||
                walk.GetFloorIndex() < 0 ||
                walk.GetFloorIndex() >=
                    Hospital.Instance.m_floors.Count)
            {
                PorterDiagnostics.Log(
                    "cart park rejected: porter=" +
                    (porter == null
                        ? "UNKNOWN"
                        : (porter.Name ?? string.Empty).Trim()) +
                    "; phase=" +
                    (job == null
                        ? "NONE"
                        : job.Phase.ToString()) +
                    "; moving=" +
                    (cart != null && cart.m_state.m_moving) +
                    "; attached=" +
                    (cart != null && cart.IsAttachedToCharacter()) +
                    "; userMatches=" +
                    (cart != null && cart.User == porter) +
                    ".");
                return false;
            }

            Vector2i parkingTile =
                walk.GetCurrentTile();
            Vector2i destinationTile =
                walk.GetDestinationTile();
            if (parkingTile != destinationTile)
            {
                PorterDiagnostics.Log(
                    "cart park rejected before destination: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; phase=" +
                    job.Phase +
                    "; current=" +
                    parkingTile +
                    "; destination=" +
                    destinationTile +
                    "; floor=" +
                    walk.GetFloorIndex() +
                    ".");
                return false;
            }

            if (job.HasCartParkingTarget &&
                (job.CartParkingFloorIndex != walk.GetFloorIndex() ||
                 job.CartParkingTarget != parkingTile))
            {
                PorterDiagnostics.Log(
                    "cart park target mismatch: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; phase=" +
                    job.Phase +
                    "; current=" +
                    parkingTile +
                    "; reserved=" +
                    job.CartParkingTarget +
                    "; currentFloor=" +
                    walk.GetFloorIndex() +
                    "; reservedFloor=" +
                    job.CartParkingFloorIndex +
                    ".");
                return false;
            }

            Room parkingRoom =
                MapScriptInterface.Instance.GetRoomAt(
                    parkingTile,
                    walk.GetFloorIndex());
            Floor parkingFloor =
                Hospital.Instance.m_floors[
                    walk.GetFloorIndex()];
            AnimModelComponent animation =
                porter.GetComponent<AnimModelComponent>();
            Direction safeDirection;
            Direction preferredDirection =
                animation == null ||
                animation.m_state == null
                    ? Direction.None
                    : animation.m_state.m_direction;

            if (parkingRoom == null ||
                !TryGetInteractionParkingCartDirection(
                    porter,
                    cart,
                    parkingFloor,
                    parkingRoom,
                    parkingTile,
                    preferredDirection,
                    out safeDirection) ||
                PorterSampleTransportRuntime.IsCartParkingTargetClaimed(
                    porter,
                    parkingTile,
                    walk.GetFloorIndex()) ||
                !MapScriptInterface.Instance.CanMoveObject(
                    cart,
                    parkingTile))
            {
                PorterDiagnostics.Log(
                    "cart park target invalidated: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; phase=" +
                    job.Phase +
                    "; tile=" +
                    parkingTile +
                    "; floor=" +
                    walk.GetFloorIndex() +
                    "; claimedByOther=" +
                    PorterSampleTransportRuntime.IsCartParkingTargetClaimed(
                        porter,
                        parkingTile,
                        walk.GetFloorIndex()) +
                    ".");
                return false;
            }

            if (animation != null &&
                animation.m_state != null &&
                safeDirection != Direction.None &&
                animation.m_state.m_direction != safeDirection)
            {
                animation.SetDirection(
                    safeDirection);
            }

            cart.StopSounds();
            if (!MapScriptInterface.Instance.MoveObject(
                    cart,
                    parkingTile))
            {
                PorterDiagnostics.Log(
                    "cart park MoveObject failed after validation: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; phase=" +
                    job.Phase +
                    "; tile=" +
                    parkingTile +
                    "; floor=" +
                    walk.GetFloorIndex() +
                    ".");
                return false;
            }

            PorterSampleTransportRuntime.ClearCartParkingTarget(
                job);

            cart.SetAttachedToCharacter(attached: false);
            cart.m_state.m_originalFloorIndex =
                job.CartHomeFloorIndex;

            Room parkedRoom =
                MapScriptInterface.Instance.GetRoomAt(
                    parkingTile,
                    cart.GetFloorIndex());
            Department parkingDepartment =
                parkedRoom == null ||
                parkedRoom.m_roomPersistentData.m_department == null
                    ? null
                    : parkedRoom.m_roomPersistentData.m_department.GetEntity();
            SetCartDepartment(
                cart,
                parkingDepartment);

            bool parked =
                !cart.m_state.m_moving &&
                !cart.IsAttachedToCharacter() &&
                cart.User == porter;

            PorterDiagnostics.Log(
                "cart parked: porter=" +
                (porter.Name ?? string.Empty).Trim() +
                "; phase=" +
                job.Phase +
                "; success=" +
                parked +
                "; tile=" +
                cart.m_state.m_position +
                "; floor=" +
                cart.GetFloorIndex() +
                "; roomType=" +
                (parkedRoom == null ||
                 parkedRoom.m_roomPersistentData == null ||
                 parkedRoom.m_roomPersistentData.m_roomType == null ||
                 parkedRoom.m_roomPersistentData.m_roomType.Entry == null
                    ? "NONE"
                    : parkedRoom.m_roomPersistentData
                        .m_roomType.Entry.DatabaseID.ToString()) +
                ".");
            return parked;
        }

        internal static bool TryWalkToParkedCart(
            Entity porter,
            PorterSampleTransportJob job)
        {
            TileObject cart =
                job == null
                    ? null
                    : job.Cart;
            WalkComponent walk =
                porter == null
                    ? null
                    : porter.GetComponent<WalkComponent>();
            if (porter == null ||
                cart == null ||
                walk == null ||
                cart.User != porter ||
                cart.m_state.m_moving)
            {
                return false;
            }

            walk.SetDestination(
                cart.GetDefaultUsePosition(),
                cart.GetFloorIndex());
            return true;
        }

        private const float InteractionParkingDoorPenalty = 100f;
        private const float InteractionParkingWallPreferenceBonus = 10f;

        private static bool TryFindPreferredInteractionParkingTile(
            Entity porter,
            TileObject cart,
            TileObject interactionObject,
            Room interactionRoom,
            out Vector2i parkingTile)
        {
            parkingTile = Vector2i.ZERO_VECTOR;
            if (porter == null ||
                cart == null ||
                interactionObject == null ||
                interactionRoom == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null ||
                GridMap.GetInstance() == null)
            {
                return false;
            }

            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            int floorIndex =
                interactionRoom.GetFloorIndex();
            if (walk == null ||
                floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Floor floor =
                Hospital.Instance.m_floors[floorIndex];
            Vector2i interactionPosition =
                interactionObject.m_state.m_position;
            Vector2f nativeUsePosition =
                interactionObject.GetDefaultUsePosition();
            Vector2i usePosition =
                new Vector2i(
                    (int)(nativeUsePosition.m_x + 0.5f),
                    (int)(nativeUsePosition.m_y + 0.5f));

            float bestScore =
                float.MaxValue;
            float bestRoute =
                float.MaxValue;

            for (int x =
                    interactionRoom.m_roomPersistentData
                        .m_positionBottom.m_x;
                x <=
                    interactionRoom.m_roomPersistentData
                        .m_positionTop.m_x;
                x++)
            {
                for (int y =
                        interactionRoom.m_roomPersistentData
                            .m_positionBottom.m_y;
                    y <=
                        interactionRoom.m_roomPersistentData
                            .m_positionTop.m_y;
                    y++)
                {
                    Vector2i candidate =
                        new Vector2i(x, y);
                    if (candidate == interactionPosition ||
                        candidate == usePosition ||
                        !IsInteractionParkingTileSafe(
                            porter,
                            cart,
                            interactionRoom,
                            candidate,
                            requireRoute: false))
                    {
                        continue;
                    }

                    int dx =
                        x - interactionPosition.m_x;
                    int dy =
                        y - interactionPosition.m_y;
                    float score =
                        dx * dx + dy * dy;

                    if (HasNearbyInteractionDoor(
                            floor,
                            candidate))
                    {
                        score +=
                            InteractionParkingDoorPenalty;
                    }
                    else if (HasAdjacentInteractionWall(
                                 floor,
                                 candidate))
                    {
                        score -=
                            InteractionParkingWallPreferenceBonus;
                    }

                    // Only calculate an exact route for a candidate that can
                    // still improve the current geometric/door/wall score.
                    if (score > bestScore)
                    {
                        continue;
                    }

                    float route =
                        GridMap.GetInstance().GetDistance(
                            walk.GetFloorIndex(),
                            walk.GetCurrentTile(),
                            floorIndex,
                            candidate,
                            AccessRights.STAFF);
                    if (route < 0f ||
                        (score == bestScore &&
                         route >= bestRoute))
                    {
                        continue;
                    }

                    bestScore = score;
                    bestRoute = route;
                    parkingTile = candidate;
                }
            }

            return parkingTile !=
                Vector2i.ZERO_VECTOR;
        }

        private static bool IsInteractionParkingTileSafe(
            Entity porter,
            TileObject cart,
            Room interactionRoom,
            Vector2i porterPosition,
            bool requireRoute)
        {
            if (porter == null ||
                cart == null ||
                interactionRoom == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null ||
                GridMap.GetInstance() == null)
            {
                return false;
            }

            int floorIndex =
                interactionRoom.GetFloorIndex();
            if (floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Floor floor =
                Hospital.Instance.m_floors[floorIndex];
            if (porterPosition.m_x < 0 ||
                porterPosition.m_y < 0 ||
                porterPosition.m_x >= floor.Size.m_x ||
                porterPosition.m_y >= floor.Size.m_y ||
                !interactionRoom.IsPositionInRoom(
                    porterPosition) ||
                !GridMap.GetInstance().IsInGrid(
                    porterPosition,
                    floorIndex) ||
                !IsCartPlacementCompletelyFree(
                    cart,
                    floor,
                    porterPosition) ||
                PorterSampleTransportRuntime
                    .IsCartParkingTargetClaimed(
                        porter,
                        porterPosition,
                        floorIndex))
            {
                return false;
            }

            Entity tileUser =
                floor.m_mapPersistentData
                    .m_tiles[
                        porterPosition.m_x,
                        porterPosition.m_y]
                    .m_user == null
                        ? null
                        : floor.m_mapPersistentData
                            .m_tiles[
                                porterPosition.m_x,
                                porterPosition.m_y]
                            .m_user.GetEntity();
            if (tileUser != null &&
                tileUser != porter)
            {
                return false;
            }

            Direction direction;
            if (!TryGetInteractionParkingCartDirection(
                    porter,
                    cart,
                    floor,
                    interactionRoom,
                    porterPosition,
                    Direction.None,
                    out direction))
            {
                return false;
            }

            if (!requireRoute)
            {
                return true;
            }

            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            return walk != null &&
                GridMap.GetInstance().GetDistance(
                    walk.GetFloorIndex(),
                    walk.GetCurrentTile(),
                    floorIndex,
                    porterPosition,
                    AccessRights.STAFF) >= 0f;
        }

        private static bool TryGetInteractionParkingCartDirection(
            Entity porter,
            TileObject cart,
            Floor floor,
            Room interactionRoom,
            Vector2i porterPosition,
            Direction preferredDirection,
            out Direction direction)
        {
            direction =
                Direction.None;
            if (cart == null ||
                floor == null ||
                interactionRoom == null)
            {
                return false;
            }

            if (preferredDirection != Direction.None &&
                IsInteractionParkingCartDirectionSafe(
                    porter,
                    cart,
                    floor,
                    interactionRoom,
                    porterPosition,
                    preferredDirection))
            {
                direction =
                    preferredDirection;
                return true;
            }

            Direction[] directions =
                new Direction[]
                {
                    Direction.NW,
                    Direction.NE,
                    Direction.SE,
                    Direction.SW
                };

            for (int i = 0;
                i < directions.Length;
                i++)
            {
                Direction candidate =
                    directions[i];
                if (candidate ==
                    preferredDirection)
                {
                    continue;
                }

                if (IsInteractionParkingCartDirectionSafe(
                        porter,
                        cart,
                        floor,
                        interactionRoom,
                        porterPosition,
                        candidate))
                {
                    direction =
                        candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool IsInteractionParkingCartDirectionSafe(
            Entity porter,
            TileObject cart,
            Floor floor,
            Room interactionRoom,
            Vector2i porterPosition,
            Direction direction)
        {
            if (GridMap.GetInstance() == null)
            {
                return false;
            }

            Vector2f offset =
                TileObject.TransformOffsetForDirection(
                    cart.GetCharacterAttachmentOffset(),
                    direction);
            Vector2i cartTile =
                new Vector2f(porterPosition)
                    .add(offset)
                    .ToVector2i();

            if (cartTile.m_x < 0 ||
                cartTile.m_y < 0 ||
                cartTile.m_x >= floor.Size.m_x ||
                cartTile.m_y >= floor.Size.m_y ||
                !interactionRoom.IsPositionInRoom(
                    cartTile) ||
                !GridMap.GetInstance().IsInGrid(
                    cartTile,
                    floor.m_floorIndex) ||
                (cartTile != porterPosition &&
                 !IsCartPlacementCompletelyFree(
                     cart,
                     floor,
                     cartTile)) ||
                (cartTile != porterPosition &&
                 !floor.IsAccessibleIgnoreDoors(
                     porterPosition,
                     cartTile)))
            {
                return false;
            }

            Entity cartTileUser =
                floor.m_mapPersistentData
                    .m_tiles[
                        cartTile.m_x,
                        cartTile.m_y]
                    .m_user == null
                        ? null
                        : floor.m_mapPersistentData
                            .m_tiles[
                                cartTile.m_x,
                                cartTile.m_y]
                            .m_user.GetEntity();

            return cartTileUser == null ||
                cartTileUser == porter;
        }

        private static bool HasNearbyInteractionDoor(
            Floor floor,
            Vector2i position)
        {
            if (floor == null)
            {
                return false;
            }

            for (int x =
                    position.m_x - 1;
                x <=
                    position.m_x + 1;
                x++)
            {
                for (int y =
                        position.m_y - 1;
                    y <=
                        position.m_y + 1;
                    y++)
                {
                    if (x < 0 ||
                        y < 0 ||
                        x >= floor.Size.m_x ||
                        y >= floor.Size.m_y)
                    {
                        continue;
                    }

                    TileWalls walls =
                        floor.GetWallsSafe(x, y);
                    if (walls != null &&
                        (walls.m_doorSW != null ||
                         walls.m_doorSE != null))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasAdjacentInteractionWall(
            Floor floor,
            Vector2i position)
        {
            if (floor == null ||
                position.m_x < 0 ||
                position.m_y < 0 ||
                position.m_x >= floor.Size.m_x ||
                position.m_y >= floor.Size.m_y)
            {
                return false;
            }

            TileWalls current =
                floor.GetWallsSafe(
                    position.m_x,
                    position.m_y);
            TileWalls north =
                position.m_y + 1 < floor.Size.m_y
                    ? floor.GetWallsSafe(
                        position.m_x,
                        position.m_y + 1)
                    : null;
            TileWalls east =
                position.m_x + 1 < floor.Size.m_x
                    ? floor.GetWallsSafe(
                        position.m_x + 1,
                        position.m_y)
                    : null;

            return
                (current != null &&
                 (current.m_wallSW != null ||
                  current.m_wallSE != null)) ||
                (north != null &&
                 north.m_wallSW != null) ||
                (east != null &&
                 east.m_wallSE != null);
        }

        internal static bool RestoreCart(
            PorterSampleTransportJob job,
            Entity porter)
        {
            if (job == null || job.Cart == null)
            {
                return true;
            }

            TileObject cart = job.Cart;

            PorterDiagnostics.Log(
                "cart restore requested: porter=" +
                (porter == null
                    ? "UNKNOWN"
                    : (porter.Name ?? string.Empty).Trim()) +
                "; phase=" +
                job.Phase +
                "; cartPosition=" +
                cart.m_state.m_position +
                "; cartFloor=" +
                cart.GetFloorIndex() +
                "; home=" +
                job.CartHomeTile +
                "; homeFloor=" +
                job.CartHomeFloorIndex +
                "; moving=" +
                cart.m_state.m_moving +
                "; attached=" +
                cart.IsAttachedToCharacter() +
                ".");

            if (!cart.m_state.m_moving)
            {
                bool alreadyHome =
                    cart.GetFloorIndex() ==
                        job.CartHomeFloorIndex &&
                    cart.m_state.m_position ==
                        job.CartHomeTile;

                if (alreadyHome &&
                    Hospital.Instance != null &&
                    Hospital.Instance.m_floors != null &&
                    job.CartHomeFloorIndex >= 0 &&
                    job.CartHomeFloorIndex <
                        Hospital.Instance.m_floors.Count)
                {
                    Floor homeFloor =
                        Hospital.Instance.m_floors[
                            job.CartHomeFloorIndex];
                    Direction safeOrientation;
                    if (TryFindOriginalCartHomeOrientation(
                            cart,
                            homeFloor,
                            job.CartHomeTile,
                            job.CartHomeOrientation,
                            porter,
                            out safeOrientation))
                    {
                        cart.Orientation =
                            safeOrientation;
                        if (cart.User == porter)
                        {
                            cart.User = null;
                        }
                        cart.SetAttachedToCharacter(
                            attached: false);
                        cart.m_state.m_originalFloorIndex =
                            -1;
                        if (job.CartHomeRoom != null)
                        {
                            homeFloor.ValidateObjectsInARoom(
                                job.CartHomeRoom);
                        }

                        PorterDiagnostics.Log(
                            "cart restore completed in place: porter=" +
                            (porter == null
                                ? "UNKNOWN"
                                : (porter.Name ?? string.Empty).Trim()) +
                            "; tile=" +
                            cart.m_state.m_position +
                            "; orientation=" +
                            safeOrientation +
                            "; floor=" +
                            cart.GetFloorIndex() +
                            ".");
                        return true;
                    }
                }

                if (MapScriptInterface.Instance == null ||
                    !MapScriptInterface.Instance.PickUpObject(
                        cart))
                {
                    PorterDiagnostics.Log(
                        "cart restore pickup failed: porter=" +
                        (porter == null
                            ? "UNKNOWN"
                            : (porter.Name ?? string.Empty).Trim()) +
                        "; tile=" +
                        cart.m_state.m_position +
                        "; floor=" +
                        cart.GetFloorIndex() +
                        ".");
                    return false;
                }

                cart.SetAttachedToCharacter(
                    attached: false);
                cart.m_state.m_originalFloorIndex =
                    job.CartHomeFloorIndex;
            }

            bool restored =
                RestoreMovingCart(
                    cart,
                    job.CartHomeRoom,
                    job.CartHomeTile,
                    job.CartHomeFloorIndex,
                    job.CartHomeOrientation,
                    porter);

            PorterDiagnostics.Log(
                "cart restore result: porter=" +
                (porter == null
                    ? "UNKNOWN"
                    : (porter.Name ?? string.Empty).Trim()) +
                "; success=" +
                restored +
                "; cartPosition=" +
                cart.m_state.m_position +
                "; cartFloor=" +
                cart.GetFloorIndex() +
                "; moving=" +
                cart.m_state.m_moving +
                ".");
            return restored;
        }

        internal static void RecoverOrphanedCartForPorter(
            Entity porter)
        {
            if (!PorterIdentity.IsPorter(porter) ||
                PorterSampleTransportRuntime.IsBusy(porter))
            {
                return;
            }

            EmployeeComponent employee =
                porter.GetComponent<EmployeeComponent>();
            Room homeRoom =
                employee == null ||
                employee.m_state.m_homeRoom == null
                    ? null
                    : employee.m_state.m_homeRoom.GetEntity();
            Department department =
                employee == null
                    ? null
                    : employee.m_state.m_department.GetEntity();
            if (department == null ||
                department.m_departmentPersistentData.m_objects == null)
            {
                return;
            }

            foreach (EntityIDPointer<TileObject> pointer in
                department.m_departmentPersistentData.m_objects)
            {
                TileObject cart = pointer.GetEntity();
                if (!IsSampleCart(cart) || cart.User != porter)
                {
                    continue;
                }

                PorterDiagnostics.Log(
                    "porter orphan cart recovery candidate: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; cartPosition=" +
                    cart.m_state.m_position +
                    "; cartFloor=" +
                    cart.GetFloorIndex() +
                    "; moving=" +
                    cart.m_state.m_moving +
                    "; attached=" +
                    cart.IsAttachedToCharacter() +
                    ".");

                if (cart.m_state.m_moving)
                {
                    RecoverOrphanedMovingCart(cart);
                }
                else
                {
                    bool fixedInsideHome =
                        homeRoom != null &&
                        cart.GetFloorIndex() ==
                            homeRoom.GetFloorIndex() &&
                        homeRoom.IsPositionInRoom(
                            cart.m_state.m_position);

                    if (fixedInsideHome)
                    {
                        cart.User = null;
                        cart.SetAttachedToCharacter(
                            attached: false);
                        cart.m_state.m_originalFloorIndex =
                            -1;
                        PorterDiagnostics.Log(
                            "cart recovery: porter=" +
                            porter.Name +
                            "; action=released reservation in station.");
                        continue;
                    }

                    if (homeRoom != null &&
                        MapScriptInterface.Instance != null &&
                        MapScriptInterface.Instance.PickUpObject(
                            cart))
                    {
                        cart.m_state.m_originalFloorIndex =
                            homeRoom.GetFloorIndex();
                        bool restored =
                            RestoreMovingCart(
                                cart,
                                homeRoom,
                                homeRoom.m_roomPersistentData
                                    .m_positionBottom,
                                homeRoom.GetFloorIndex(),
                                cart.Orientation,
                                porter);

                        PorterDiagnostics.Log(
                            "cart recovery: porter=" +
                            porter.Name +
                            "; parkedCartRestored=" +
                            restored +
                            ".");
                    }
                    else
                    {
                        cart.User = null;
                        cart.SetAttachedToCharacter(
                            attached: false);
                        cart.m_state.m_originalFloorIndex =
                            -1;
                        Plugin.Log?.LogWarning(
                            "Could not return orphaned parked Porter sample cart to its station; reservation was released: porter=" +
                            porter.Name + ".");
                    }
                }
            }
        }

        internal static void RecoverOrphanedMovingCarts(
            Floor floor)
        {
            if (floor == null || floor.m_movingObjects == null)
            {
                return;
            }

            List<TileObject> moving =
                new List<TileObject>(floor.m_movingObjects);
            for (int i = 0; i < moving.Count; i++)
            {
                TileObject cart = moving[i];
                if (!IsSampleCart(cart) ||
                    PorterSampleTransportRuntime.IsCartInActiveJob(cart))
                {
                    continue;
                }

                PorterDiagnostics.Log(
                    "orphan cart recovery triggered: floor=" +
                    floor.m_floorIndex +
                    "; cartPosition=" +
                    cart.m_state.m_position +
                    "; cartFloor=" +
                    cart.GetFloorIndex() +
                    "; originalFloor=" +
                    cart.m_state.m_originalFloorIndex +
                    "; attached=" +
                    cart.IsAttachedToCharacter() +
                    "; user=" +
                    (cart.User == null
                        ? "NONE"
                        : (cart.User.Name ?? string.Empty).Trim()) +
                    ".");

                RecoverOrphanedMovingCart(cart);
            }
        }

        internal static bool HasAttachedCart(
            Entity porter)
        {
            if (porter == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
            {
                return false;
            }

            foreach (Floor floor in Hospital.Instance.m_floors)
            {
                if (floor == null || floor.m_movingObjects == null)
                {
                    continue;
                }

                for (int i = 0; i < floor.m_movingObjects.Count; i++)
                {
                    TileObject cart = floor.m_movingObjects[i];
                    if (IsSampleCart(cart) &&
                        cart.User == porter &&
                        cart.IsAttachedToCharacter())
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static void CollectFreeCarts(
            Entity porter,
            HashSet<TileObject> carts)
        {
            if (carts == null)
            {
                return;
            }

            Room homeRoom;
            FindFreeCart(porter, out homeRoom, carts);
        }

        internal static int CountSampleCartsInDepartment(
            Department department)
        {
            if (department == null ||
                department.m_departmentPersistentData == null ||
                department.m_departmentPersistentData.m_objects == null)
            {
                return 0;
            }

            HashSet<TileObject> carts = new HashSet<TileObject>();
            foreach (EntityIDPointer<TileObject> pointer in
                department.m_departmentPersistentData.m_objects)
            {
                TileObject cart = pointer.GetEntity();
                if (IsSampleCart(cart))
                {
                    carts.Add(cart);
                }
            }

            return carts.Count;
        }

        private static TileObject FindFreeCart(
            Entity porter,
            out Room homeRoom)
        {
            return FindFreeCart(
                porter,
                out homeRoom,
                null);
        }

        private static TileObject FindFreeCart(
            Entity porter,
            out Room homeRoom,
            HashSet<TileObject> allFreeCarts)
        {
            homeRoom = null;
            if (porter == null ||
                MapScriptInterface.Instance == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null ||
                GridMap.GetInstance() == null)
            {
                return null;
            }

            EmployeeComponent employee =
                porter.GetComponent<EmployeeComponent>();
            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            homeRoom =
                employee == null ||
                employee.m_state.m_homeRoom == null
                    ? null
                    : employee.m_state.m_homeRoom.GetEntity();

            if (employee == null ||
                walk == null ||
                homeRoom == null ||
                !PorterStationRegistry.IsPorterStation(
                    homeRoom.m_roomPersistentData.m_roomType.Entry))
            {
                return null;
            }

            int floorIndex =
                homeRoom.GetFloorIndex();
            if (floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return null;
            }

            Floor floor =
                Hospital.Instance.m_floors[floorIndex];
            TileObject best = null;
            float bestDistance = float.MaxValue;

            for (int x =
                    homeRoom.m_roomPersistentData.m_positionBottom.m_x;
                x <=
                    homeRoom.m_roomPersistentData.m_positionTop.m_x;
                x++)
            {
                for (int y =
                        homeRoom.m_roomPersistentData.m_positionBottom.m_y;
                    y <=
                        homeRoom.m_roomPersistentData.m_positionTop.m_y;
                    y++)
                {
                    Vector2i position =
                        new Vector2i(x, y);
                    if (!homeRoom.IsPositionInRoom(position) ||
                        (int)floor.m_mapPersistentData
                            .m_mapLogisticsLayer
                            .m_accessRights[x, y] >
                            (int)AccessRights.STAFF)
                    {
                        continue;
                    }

                    List<TileObject> objects =
                        floor.m_tileObjects[x, y]
                            .GetAllObjects();
                    for (int i = 0; i < objects.Count; i++)
                    {
                        TileObject candidate =
                            objects[i];
                        if (!IsSampleCart(candidate) ||
                            candidate.User != null ||
                            candidate.Owner != null ||
                            candidate.m_state.m_moving ||
                            candidate.IsBroken() ||
                            !candidate.IsValid())
                        {
                            continue;
                        }

                        if (!IsParkedCartPickupReachable(
                                candidate,
                                floor,
                                homeRoom,
                                porter))
                        {
                            PorterDiagnostics.Log(
                                "sample cart skipped as inaccessible: porter=" +
                                (porter.Name ?? string.Empty).Trim() +
                                "; cartPosition=" +
                                candidate.m_state.m_position +
                                "; cartFloor=" +
                                candidate.GetFloorIndex() +
                                "; orientation=" +
                                candidate.Orientation +
                                "; " +
                                GetCartAccessDiagnostic(
                                    candidate,
                                    floor,
                                    homeRoom,
                                    candidate.m_state.m_position,
                                    candidate.Orientation,
                                    porter) +
                                ".");
                            continue;
                        }

                        float distance =
                            GridMap.GetInstance().GetDistance(
                                walk.GetFloorIndex(),
                                walk.GetCurrentTile(),
                                floorIndex,
                                candidate.m_state.m_position,
                                AccessRights.STAFF);
                        if (distance < 0f)
                        {
                            continue;
                        }

                        if (allFreeCarts != null)
                        {
                            allFreeCarts.Add(candidate);
                        }

                        if (distance >= bestDistance)
                        {
                            continue;
                        }

                        best = candidate;
                        bestDistance = distance;
                    }
                }
            }

            return best;
        }

        internal static bool IsSampleCart(
            TileObject cart)
        {
            return cart != null &&
                cart.m_state != null &&
                cart.m_state.m_gameDBObject != null &&
                !cart.m_state.m_gameDBObject.IsDeleted &&
                cart.m_state.m_gameDBObject.Entry != null &&
                cart.HasTag(PorterIds.SampleCartTag);
        }

        private static void RecoverOrphanedMovingCart(
            TileObject cart)
        {
            if (!IsSampleCart(cart) ||
                !cart.m_state.m_moving ||
                MapScriptInterface.Instance == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
            {
                return;
            }

            Room homeRoom =
                TryGetOrphanCartHomeRoom(cart);
            if (homeRoom == null)
            {
                Plugin.Log?.LogWarning(
                    "Could not recover orphaned Porter sample cart because no Porter Station could be resolved.");
                return;
            }

            int homeFloorIndex =
                homeRoom.GetFloorIndex();
            if (homeFloorIndex < 0 ||
                homeFloorIndex >= Hospital.Instance.m_floors.Count)
            {
                Plugin.Log?.LogWarning(
                    "Could not recover orphaned Porter sample cart because its Porter Station floor is unavailable.");
                return;
            }

            Vector2i homeTile;
            if (homeRoom.IsPositionInRoom(
                    cart.m_state.m_position))
            {
                homeTile =
                    cart.m_state.m_position;
            }
            else
            {
                homeTile =
                    new Vector2i(
                        (homeRoom.m_roomPersistentData.m_positionBottom.m_x +
                         homeRoom.m_roomPersistentData.m_positionTop.m_x) / 2,
                        (homeRoom.m_roomPersistentData.m_positionBottom.m_y +
                         homeRoom.m_roomPersistentData.m_positionTop.m_y) / 2);
            }

            Direction homeOrientation =
                cart.Orientation;
            bool resolvedFromUser =
                IsCartUserHomeRoom(
                    cart,
                    homeRoom);

            bool restored =
                RestoreMovingCart(
                    cart,
                    homeRoom,
                    homeTile,
                    homeFloorIndex,
                    homeOrientation,
                    null);

            PorterDiagnostics.Log(
                "cart recovery: movingCartRestored=" +
                restored +
                "; stationFloor=" +
                homeFloorIndex +
                "; preferredTile=" +
                homeTile +
                "; resolvedFromUser=" +
                resolvedFromUser +
                ".");
        }

        private static bool IsCartUserHomeRoom(
            TileObject cart,
            Room room)
        {
            Entity user =
                cart == null
                    ? null
                    : cart.User;
            EmployeeComponent employee =
                user == null ||
                !PorterIdentity.IsPorter(user)
                    ? null
                    : user.GetComponent<EmployeeComponent>();
            Room userHome =
                employee == null ||
                employee.m_state.m_homeRoom == null
                    ? null
                    : employee.m_state.m_homeRoom.GetEntity();
            return userHome != null &&
                userHome == room;
        }

        private static Room TryGetOrphanCartHomeRoom(
            TileObject cart)
        {
            if (cart == null)
            {
                return null;
            }

            Entity user =
                cart.User;
            EmployeeComponent userEmployee =
                user == null ||
                !PorterIdentity.IsPorter(user)
                    ? null
                    : user.GetComponent<EmployeeComponent>();
            Room userHome =
                userEmployee == null ||
                userEmployee.m_state.m_homeRoom == null
                    ? null
                    : userEmployee.m_state.m_homeRoom.GetEntity();
            if (userHome != null &&
                userHome.m_roomPersistentData.m_roomType != null &&
                PorterStationRegistry.IsPorterStation(
                    userHome.m_roomPersistentData.m_roomType.Entry))
            {
                return userHome;
            }

            Department department =
                userEmployee == null ||
                userEmployee.m_state.m_department == null
                    ? null
                    : userEmployee.m_state.m_department.GetEntity();
            if (department == null &&
                cart.m_state.m_department != null)
            {
                department =
                    cart.m_state.m_department.GetEntity();
            }
            if (department == null ||
                department.m_departmentPersistentData.m_rooms == null)
            {
                return null;
            }

            int preferredFloor =
                cart.m_state.m_originalFloorIndex;
            Room best = null;
            int bestDistance = int.MaxValue;

            foreach (EntityIDPointer<Room> pointer in
                department.m_departmentPersistentData.m_rooms)
            {
                Room candidate =
                    pointer.GetEntity();
                if (candidate == null ||
                    candidate.m_roomPersistentData.m_roomType == null ||
                    !PorterStationRegistry.IsPorterStation(
                        candidate.m_roomPersistentData.m_roomType.Entry) ||
                    (preferredFloor >= 0 &&
                     candidate.GetFloorIndex() != preferredFloor))
                {
                    continue;
                }

                if (candidate.IsPositionInRoom(
                        cart.m_state.m_position))
                {
                    return candidate;
                }

                int centerX =
                    (candidate.m_roomPersistentData.m_positionBottom.m_x +
                     candidate.m_roomPersistentData.m_positionTop.m_x) / 2;
                int centerY =
                    (candidate.m_roomPersistentData.m_positionBottom.m_y +
                     candidate.m_roomPersistentData.m_positionTop.m_y) / 2;
                int dx =
                    centerX - cart.m_state.m_position.m_x;
                int dy =
                    centerY - cart.m_state.m_position.m_y;
                int distance =
                    dx * dx + dy * dy;
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                best = candidate;
            }

            if (best != null ||
                preferredFloor < 0)
            {
                return best;
            }

            foreach (EntityIDPointer<Room> pointer in
                department.m_departmentPersistentData.m_rooms)
            {
                Room candidate =
                    pointer.GetEntity();
                if (candidate != null &&
                    candidate.m_roomPersistentData.m_roomType != null &&
                    PorterStationRegistry.IsPorterStation(
                        candidate.m_roomPersistentData.m_roomType.Entry))
                {
                    return candidate;
                }
            }

            return null;
        }

        internal static bool IsCartPlacementCompletelyFree(
            TileObject cart,
            Floor floor,
            Vector2i position)
        {
            if (cart == null ||
                floor == null ||
                position.m_x < 0 ||
                position.m_y < 0 ||
                position.m_x >= floor.Size.m_x ||
                position.m_y >= floor.Size.m_y ||
                MapScriptInterface.Instance == null)
            {
                return false;
            }

            TileObjects tileObjects =
                floor.m_tileObjects[
                    position.m_x,
                    position.m_y];
            if (tileObjects == null ||
                tileObjects.HasAnyObject())
            {
                return false;
            }

            if (cart.GetFloorIndex() != floor.m_floorIndex)
            {
                return true;
            }

            return MapScriptInterface.Instance.CanMoveObject(
                cart,
                position);
        }

        private static bool IsTileUsedByOtherInteractableObject(
            TileObject cart,
            Floor floor,
            Room room,
            Vector2i position)
        {
            if (floor == null || room == null)
            {
                return false;
            }

            for (int x =
                    room.m_roomPersistentData.m_positionBottom.m_x;
                x <=
                    room.m_roomPersistentData.m_positionTop.m_x;
                x++)
            {
                for (int y =
                        room.m_roomPersistentData.m_positionBottom.m_y;
                    y <=
                        room.m_roomPersistentData.m_positionTop.m_y;
                    y++)
                {
                    Vector2i tile =
                        new Vector2i(x, y);
                    if (!room.IsPositionInRoom(tile))
                    {
                        continue;
                    }

                    List<TileObject> objects =
                        floor.m_tileObjects[
                            x,
                            y].GetAllAccessibleObjects();
                    for (int i = 0; i < objects.Count; i++)
                    {
                        TileObject other =
                            objects[i];
                        if (other == null ||
                            other == cart ||
                            other.m_state == null ||
                            other.m_state.m_gameDBObject == null ||
                            other.m_state.m_gameDBObject.Entry == null ||
                            other.m_state.m_gameDBObject.Entry.NotInteractable)
                        {
                            continue;
                        }

                        if (other.CanBeAccessedFrom(
                                position,
                                floor))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsParkedCartPickupReachable(
            TileObject cart,
            Floor floor,
            Room homeRoom,
            Entity porter)
        {
            if (cart == null ||
                floor == null ||
                homeRoom == null ||
                porter == null ||
                cart.m_state == null ||
                cart.m_state.m_gameDBObject == null ||
                cart.m_state.m_gameDBObject.Entry == null ||
                GridMap.GetInstance() == null)
            {
                return false;
            }

            Vector2i[] accessPositions =
                cart.m_state.m_gameDBObject.Entry.AccessPositions;
            if (accessPositions == null ||
                accessPositions.Length == 0)
            {
                accessPositions =
                    new Vector2i[]
                    {
                        new Vector2i(-1, 0)
                    };
            }

            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            if (walk == null)
            {
                return false;
            }

            if (walk.IsSitting() &&
                cart.m_state.m_position == walk.GetCurrentTile())
            {
                PorterDiagnostics.Log(
                    "sample cart skipped for seated Porter: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; cartPosition=" +
                    cart.m_state.m_position +
                    "; porterTile=" +
                    walk.GetCurrentTile() +
                    "; reason=cart-overlaps-seated-porter.");
                return false;
            }

            for (int i = 0; i < accessPositions.Length; i++)
            {
                Vector2i accessTile =
                    cart.m_state.m_position +
                    TileObject.TransformUsePositionForDirection(
                        accessPositions[i],
                        cart.Orientation);

                if (accessTile.m_x < 0 ||
                    accessTile.m_y < 0 ||
                    accessTile.m_x >= floor.Size.m_x ||
                    accessTile.m_y >= floor.Size.m_y ||
                    !cart.CanBeAccessedFrom(
                        accessTile,
                        floor) ||
                    !GridMap.GetInstance().IsInGrid(
                        accessTile,
                        floor.m_floorIndex))
                {
                    continue;
                }

                Entity tileUser =
                    floor.m_mapPersistentData
                        .m_tiles[
                            accessTile.m_x,
                            accessTile.m_y]
                        .m_user == null
                            ? null
                            : floor.m_mapPersistentData
                                .m_tiles[
                                    accessTile.m_x,
                                    accessTile.m_y]
                                .m_user.GetEntity();
                if (tileUser != null &&
                    tileUser != porter)
                {
                    continue;
                }

                if (GridMap.GetInstance().GetDistance(
                        walk.GetFloorIndex(),
                        walk.GetCurrentTile(),
                        floor.m_floorIndex,
                        accessTile,
                        AccessRights.STAFF) >= 0f)
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetCartAccessDiagnostic(
            TileObject cart,
            Floor floor,
            Room homeRoom,
            Vector2i placement,
            Direction orientation,
            Entity accessCharacter)
        {
            if (cart == null)
            {
                return "reason=cart-null";
            }
            if (floor == null)
            {
                return "reason=floor-null";
            }
            if (homeRoom == null)
            {
                return "reason=room-null";
            }
            if (cart.m_state == null ||
                cart.m_state.m_gameDBObject == null ||
                cart.m_state.m_gameDBObject.Entry == null)
            {
                return "reason=cart-db-invalid";
            }

            Vector2i[] accessPositions =
                cart.m_state.m_gameDBObject.Entry.AccessPositions;
            if (accessPositions == null ||
                accessPositions.Length == 0)
            {
                accessPositions =
                    new Vector2i[]
                    {
                        new Vector2i(-1, 0)
                    };
            }

            WalkComponent walk =
                accessCharacter == null
                    ? null
                    : accessCharacter.GetComponent<WalkComponent>();
            GridMap grid =
                GridMap.GetInstance();

            string result = string.Empty;
            for (int i = 0; i < accessPositions.Length; i++)
            {
                Vector2i accessTile =
                    placement +
                    TileObject.TransformUsePositionForDirection(
                        accessPositions[i],
                        orientation);

                string reason = "unknown";
                if (accessTile.m_x < 0 ||
                    accessTile.m_y < 0 ||
                    accessTile.m_x >= floor.Size.m_x ||
                    accessTile.m_y >= floor.Size.m_y)
                {
                    reason = "out-of-map";
                }
                else if (!cart.CanBeAccessedFrom(
                            accessTile,
                            floor))
                {
                    reason = "vanilla-access-rejected";
                }
                else if (grid == null)
                {
                    reason = "grid-null";
                }
                else if (!grid.IsInGrid(
                            accessTile,
                            floor.m_floorIndex))
                {
                    reason = "not-in-grid";
                }
                else
                {
                    Entity accessTileUser =
                        floor.m_mapPersistentData
                            .m_tiles[
                                accessTile.m_x,
                                accessTile.m_y]
                            .m_user == null
                                ? null
                                : floor.m_mapPersistentData
                                    .m_tiles[
                                        accessTile.m_x,
                                        accessTile.m_y]
                                    .m_user.GetEntity();
                    if (accessTileUser != null &&
                        accessTileUser != accessCharacter)
                    {
                        reason =
                            "tile-user:" +
                            (accessTileUser.Name ?? string.Empty).Trim();
                    }
                    else if (walk != null)
                    {
                        float distance =
                            grid.GetDistance(
                                walk.GetFloorIndex(),
                                walk.GetCurrentTile(),
                                floor.m_floorIndex,
                                accessTile,
                                AccessRights.STAFF);
                        reason =
                            distance < 0f
                                ? "no-staff-path"
                                : "reachable-distance=" + distance;
                    }
                    else
                    {
                        reason = "reachable-no-character-check";
                    }
                }

                if (result.Length > 0)
                {
                    result += "; ";
                }
                result +=
                    "access[" +
                    i +
                    "]=" +
                    accessTile +
                    ":" +
                    reason;
            }

            return result;
        }

        private static bool IsOriginalCartHomeAccessReachable(
            TileObject cart,
            Floor floor,
            Vector2i placement,
            Direction orientation,
            Entity accessCharacter)
        {
            if (cart == null ||
                floor == null ||
                cart.m_state == null ||
                cart.m_state.m_gameDBObject == null ||
                cart.m_state.m_gameDBObject.Entry == null)
            {
                return false;
            }

            Vector2i[] accessPositions =
                cart.m_state.m_gameDBObject.Entry.AccessPositions;
            if (accessPositions == null ||
                accessPositions.Length == 0)
            {
                accessPositions =
                    new Vector2i[]
                    {
                        new Vector2i(-1, 0)
                    };
            }

            WalkComponent walk =
                accessCharacter == null
                    ? null
                    : accessCharacter.GetComponent<WalkComponent>();
            GridMap grid =
                GridMap.GetInstance();
            if (grid == null)
            {
                return false;
            }

            foreach (Vector2i localAccess in accessPositions)
            {
                Vector2i accessTile =
                    placement +
                    TileObject.TransformUsePositionForDirection(
                        localAccess,
                        orientation);
                if (accessTile.m_x < 0 ||
                    accessTile.m_y < 0 ||
                    accessTile.m_x >= floor.Size.m_x ||
                    accessTile.m_y >= floor.Size.m_y ||
                    floor.IsAnyBlockingObjectAt(accessTile) ||
                    !floor.IsAccessibleIgnoreDoors(
                        accessTile,
                        placement) ||
                    !grid.IsInGrid(
                        accessTile,
                        floor.m_floorIndex))
                {
                    continue;
                }

                Entity accessTileUser =
                    floor.m_mapPersistentData
                        .m_tiles[
                            accessTile.m_x,
                            accessTile.m_y]
                        .m_user == null
                            ? null
                            : floor.m_mapPersistentData
                                .m_tiles[
                                    accessTile.m_x,
                                    accessTile.m_y]
                                .m_user.GetEntity();
                if (accessTileUser != null &&
                    accessTileUser != accessCharacter)
                {
                    continue;
                }

                if (walk != null &&
                    grid.GetDistance(
                        walk.GetFloorIndex(),
                        walk.GetCurrentTile(),
                        floor.m_floorIndex,
                        accessTile,
                        AccessRights.STAFF) < 0f)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool TryFindOriginalCartHomeOrientation(
            TileObject cart,
            Floor floor,
            Vector2i placement,
            Direction preferredOrientation,
            Entity accessCharacter,
            out Direction orientation)
        {
            orientation = Direction.None;

            if (preferredOrientation != Direction.None &&
                IsOriginalCartHomeAccessReachable(
                    cart,
                    floor,
                    placement,
                    preferredOrientation,
                    accessCharacter))
            {
                orientation = preferredOrientation;
                return true;
            }

            Direction[] orientations =
                new Direction[]
                {
                    Direction.NW,
                    Direction.NE,
                    Direction.SE,
                    Direction.SW
                };

            foreach (Direction candidate in orientations)
            {
                if (candidate == preferredOrientation)
                {
                    continue;
                }

                if (IsOriginalCartHomeAccessReachable(
                        cart,
                        floor,
                        placement,
                        candidate,
                        accessCharacter))
                {
                    orientation = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool IsCartAccessReachable(
            TileObject cart,
            Floor floor,
            Room homeRoom,
            Vector2i placement,
            Direction orientation,
            Entity accessCharacter,
            bool rejectFunctionalOverlap)
        {
            if (cart == null ||
                floor == null ||
                homeRoom == null ||
                cart.m_state == null ||
                cart.m_state.m_gameDBObject == null ||
                cart.m_state.m_gameDBObject.Entry == null ||
                !homeRoom.IsPositionInRoom(placement))
            {
                return false;
            }

            if (rejectFunctionalOverlap &&
                IsTileUsedByOtherInteractableObject(
                    cart,
                    floor,
                    homeRoom,
                    placement))
            {
                return false;
            }

            GridMap grid =
                GridMap.GetInstance();
            WalkComponent walk =
                accessCharacter == null
                    ? null
                    : accessCharacter.GetComponent<WalkComponent>();

            if (walk != null &&
                (grid == null ||
                 !grid.IsInGrid(
                     placement,
                     floor.m_floorIndex) ||
                 grid.GetDistance(
                     walk.GetFloorIndex(),
                     walk.GetCurrentTile(),
                     floor.m_floorIndex,
                     placement,
                     AccessRights.STAFF) < 0f))
            {
                return false;
            }

            Vector2i[] accessPositions =
                cart.m_state.m_gameDBObject.Entry.AccessPositions;
            if (accessPositions == null ||
                accessPositions.Length == 0)
            {
                accessPositions =
                    new Vector2i[]
                    {
                        new Vector2i(-1, 0)
                    };
            }

            foreach (Vector2i localAccess in accessPositions)
            {
                Vector2i accessTile =
                    placement +
                    TileObject.TransformUsePositionForDirection(
                        localAccess,
                        orientation);

                if (accessTile.m_x < 0 ||
                    accessTile.m_y < 0 ||
                    accessTile.m_x >= floor.Size.m_x ||
                    accessTile.m_y >= floor.Size.m_y ||
                    floor.IsAnyBlockingObjectAt(accessTile) ||
                    !floor.IsAccessibleIgnoreDoors(
                        accessTile,
                        placement))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool TryFindAccessibleCartOrientation(
            TileObject cart,
            Floor floor,
            Room homeRoom,
            Vector2i placement,
            Direction preferredOrientation,
            Entity accessCharacter,
            bool rejectFunctionalOverlap,
            out Direction orientation)
        {
            orientation = Direction.None;

            if (preferredOrientation != Direction.None &&
                IsCartAccessReachable(
                    cart,
                    floor,
                    homeRoom,
                    placement,
                    preferredOrientation,
                    accessCharacter,
                    rejectFunctionalOverlap))
            {
                orientation = preferredOrientation;
                return true;
            }

            Direction[] orientations =
                new Direction[]
                {
                    Direction.NW,
                    Direction.NE,
                    Direction.SE,
                    Direction.SW
                };

            foreach (Direction candidate in orientations)
            {
                if (candidate == preferredOrientation)
                {
                    continue;
                }

                if (IsCartAccessReachable(
                        cart,
                        floor,
                        homeRoom,
                        placement,
                        candidate,
                        accessCharacter,
                        rejectFunctionalOverlap))
                {
                    orientation = candidate;
                    return true;
                }
            }

            return false;
        }

        internal static bool TryGetSafeReturnPlacement(
            PorterSampleTransportJob job,
            Entity accessCharacter,
            out Vector2i placement,
            out Direction orientation)
        {
            placement = Vector2i.ZERO_VECTOR;
            orientation = Direction.None;

            if (job == null ||
                !IsSampleCart(job.Cart) ||
                job.CartHomeRoom == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null ||
                job.CartHomeFloorIndex < 0 ||
                job.CartHomeFloorIndex >=
                    Hospital.Instance.m_floors.Count ||
                job.CartHomeRoom.GetFloorIndex() !=
                    job.CartHomeFloorIndex)
            {
                return false;
            }

            Floor floor =
                Hospital.Instance.m_floors[
                    job.CartHomeFloorIndex];
            if (job.CartHomeRoom.IsPositionInRoom(
                    job.CartHomeTile) &&
                IsCartPlacementCompletelyFree(
                    job.Cart,
                    floor,
                    job.CartHomeTile) &&
                TryFindOriginalCartHomeOrientation(
                    job.Cart,
                    floor,
                    job.CartHomeTile,
                    job.CartHomeOrientation,
                    accessCharacter,
                    out orientation))
            {
                placement = job.CartHomeTile;
                return true;
            }

            return TryFindFallbackPlacement(
                job.Cart,
                job.CartHomeRoom,
                job.CartHomeTile,
                job.CartHomeOrientation,
                accessCharacter,
                out placement,
                out orientation);
        }

        internal static bool HasSafeReturnPlacement(
            PorterSampleTransportJob job,
            Entity accessCharacter)
        {
            Vector2i placement;
            Direction orientation;
            return TryGetSafeReturnPlacement(
                job,
                accessCharacter,
                out placement,
                out orientation);
        }

        internal static string GetSafeReturnPlacementDiagnostic(
            PorterSampleTransportJob job)
        {
            if (job == null)
            {
                return "returnReason=job-null";
            }
            if (!IsSampleCart(job.Cart))
            {
                return "returnReason=cart-invalid";
            }
            if (job.CartHomeRoom == null)
            {
                return "returnReason=home-room-null";
            }
            if (Hospital.Instance == null ||
                Hospital.Instance.m_floors == null ||
                job.CartHomeFloorIndex < 0 ||
                job.CartHomeFloorIndex >=
                    Hospital.Instance.m_floors.Count)
            {
                return "returnReason=home-floor-invalid";
            }
            if (job.CartHomeRoom.GetFloorIndex() !=
                job.CartHomeFloorIndex)
            {
                return "returnReason=home-room-floor-mismatch";
            }

            Floor floor =
                Hospital.Instance.m_floors[
                    job.CartHomeFloorIndex];
            bool homeInRoom =
                job.CartHomeRoom.IsPositionInRoom(
                    job.CartHomeTile);
            bool homeFree =
                homeInRoom &&
                IsCartPlacementCompletelyFree(
                    job.Cart,
                    floor,
                    job.CartHomeTile);
            Direction homeOrientation;
            bool homeAccessible =
                homeFree &&
                TryFindOriginalCartHomeOrientation(
                    job.Cart,
                    floor,
                    job.CartHomeTile,
                    job.CartHomeOrientation,
                    null,
                    out homeOrientation);

            int roomTiles = 0;
            int fallbackFreeTiles = 0;
            int fallbackPreferredSafeTiles = 0;
            int fallbackNativeSafeTiles = 0;

            for (int x =
                    job.CartHomeRoom.m_roomPersistentData.m_positionBottom.m_x;
                x <=
                    job.CartHomeRoom.m_roomPersistentData.m_positionTop.m_x;
                x++)
            {
                for (int y =
                        job.CartHomeRoom.m_roomPersistentData.m_positionBottom.m_y;
                    y <=
                        job.CartHomeRoom.m_roomPersistentData.m_positionTop.m_y;
                    y++)
                {
                    Vector2i candidate =
                        new Vector2i(x, y);
                    if (!job.CartHomeRoom.IsPositionInRoom(
                            candidate))
                    {
                        continue;
                    }

                    roomTiles++;
                    if (!IsCartPlacementCompletelyFree(
                            job.Cart,
                            floor,
                            candidate))
                    {
                        continue;
                    }

                    fallbackFreeTiles++;
                    Direction fallbackOrientation;
                    if (TryFindAccessibleCartOrientation(
                            job.Cart,
                            floor,
                            job.CartHomeRoom,
                            candidate,
                            job.CartHomeOrientation,
                            null,
                            true,
                            out fallbackOrientation))
                    {
                        fallbackPreferredSafeTiles++;
                    }

                    if (TryFindAccessibleCartOrientation(
                            job.Cart,
                            floor,
                            job.CartHomeRoom,
                            candidate,
                            job.CartHomeOrientation,
                            null,
                            false,
                            out fallbackOrientation))
                    {
                        fallbackNativeSafeTiles++;
                    }
                }
            }

            return
                "home=" +
                job.CartHomeTile +
                "; homeOrientation=" +
                job.CartHomeOrientation +
                "; homeInRoom=" +
                homeInRoom +
                "; homeFree=" +
                homeFree +
                "; homeAccessible=" +
                homeAccessible +
                "; roomTiles=" +
                roomTiles +
                "; fallbackFreeTiles=" +
                fallbackFreeTiles +
                "; fallbackPreferredSafeTiles=" +
                fallbackPreferredSafeTiles +
                "; fallbackNativeSafeTiles=" +
                fallbackNativeSafeTiles;
        }

        private static bool RestoreMovingCart(
            TileObject cart,
            Room homeRoom,
            Vector2i homeTile,
            int homeFloorIndex,
            Direction homeOrientation,
            Entity accessCharacter)
        {
            Department homeDepartment =
                homeRoom == null ||
                homeRoom.m_roomPersistentData.m_department == null
                    ? null
                    : homeRoom.m_roomPersistentData.m_department.GetEntity();
            SetCartDepartment(
                cart,
                homeDepartment);

            if (!IsSampleCart(cart) ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null ||
                homeFloorIndex < 0 ||
                homeFloorIndex >= Hospital.Instance.m_floors.Count ||
                MapScriptInterface.Instance == null)
            {
                return false;
            }

            cart.StopSounds();

            MoveCartToFloor(cart, homeFloorIndex);

            Floor homeFloor =
                Hospital.Instance.m_floors[
                    homeFloorIndex];
            Vector2i placement = homeTile;
            Direction placementOrientation =
                homeOrientation;
            bool canPlace =
                homeRoom != null &&
                homeRoom.GetFloorIndex() == homeFloorIndex &&
                homeRoom.IsPositionInRoom(homeTile) &&
                IsCartPlacementCompletelyFree(
                    cart,
                    homeFloor,
                    homeTile) &&
                TryFindOriginalCartHomeOrientation(
                    cart,
                    homeFloor,
                    homeTile,
                    homeOrientation,
                    accessCharacter,
                    out placementOrientation);

            if (!canPlace &&
                homeRoom != null &&
                homeRoom.GetFloorIndex() == homeFloorIndex)
            {
                canPlace =
                    TryFindFallbackPlacement(
                        cart,
                        homeRoom,
                        homeTile,
                        homeOrientation,
                        accessCharacter,
                        out placement,
                        out placementOrientation);
            }

            if (!canPlace ||
                !MapScriptInterface.Instance.MoveObject(
                    cart,
                    placement))
            {
                PorterDiagnostics.Log(
                    "cart restore placement deferred: requestedHome=" +
                    homeTile +
                    "; selectedPlacement=" +
                    placement +
                    "; homeFloor=" +
                    homeFloorIndex +
                    "; canPlace=" +
                    canPlace +
                    "; cartPosition=" +
                    cart.m_state.m_position +
                    "; cartFloor=" +
                    cart.GetFloorIndex() +
                    "; attached=" +
                    cart.IsAttachedToCharacter() +
                    "; user=" +
                    (cart.User == null
                        ? "NONE"
                        : (cart.User.Name ?? string.Empty).Trim()) +
                    ".");
                return false;
            }

            Vector2i accessTile =
                TileObject.GetDefaultUsePositionForDirection(
                    placement,
                    placementOrientation);

            PorterDiagnostics.Log(
                "cart restore placement: requestedHome=" +
                homeTile +
                "; selectedPlacement=" +
                placement +
                "; selectedOrientation=" +
                placementOrientation +
                "; accessTile=" +
                accessTile +
                "; homeFloor=" +
                homeFloorIndex +
                "; usedFallback=" +
                (placement != homeTile ||
                    placementOrientation != homeOrientation) +
                ".");

            cart.Orientation = placementOrientation;
            cart.User = null;
            cart.SetAttachedToCharacter(attached: false);
            cart.m_state.m_originalFloorIndex = -1;
            cart.StopSounds();
            if (homeRoom != null)
            {
                homeFloor.ValidateObjectsInARoom(homeRoom);
            }
            return true;
        }

        private static void MoveCartToFloor(
            TileObject cart,
            int floorIndex)
        {
            int currentFloorIndex =
                cart.GetFloorIndex();
            if (currentFloorIndex == floorIndex)
            {
                return;
            }

            if (currentFloorIndex >= 0 &&
                currentFloorIndex < Hospital.Instance.m_floors.Count)
            {
                Hospital.Instance.m_floors[currentFloorIndex]
                    .m_movingObjects.Remove(cart);
            }

            Floor target =
                Hospital.Instance.m_floors[floorIndex];
            if (!target.m_movingObjects.Contains(cart))
            {
                target.m_movingObjects.Add(cart);
            }

            cart.m_state.m_floorIndex = floorIndex;
        }

        private static bool TryFindFallbackPlacement(
            TileObject cart,
            Room homeRoom,
            Vector2i homeTile,
            Direction homeOrientation,
            Entity accessCharacter,
            out Vector2i placement,
            out Direction orientation)
        {
            if (TryFindFallbackPlacementPass(
                    cart,
                    homeRoom,
                    homeTile,
                    homeOrientation,
                    accessCharacter,
                    true,
                    out placement,
                    out orientation))
            {
                return true;
            }

            return TryFindFallbackPlacementPass(
                cart,
                homeRoom,
                homeTile,
                homeOrientation,
                accessCharacter,
                false,
                out placement,
                out orientation);
        }

        private static bool TryFindFallbackPlacementPass(
            TileObject cart,
            Room homeRoom,
            Vector2i homeTile,
            Direction homeOrientation,
            Entity accessCharacter,
            bool rejectFunctionalOverlap,
            out Vector2i placement,
            out Direction orientation)
        {
            placement = Vector2i.ZERO_VECTOR;
            orientation = Direction.None;
            if (cart == null ||
                homeRoom == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
            {
                return false;
            }

            Floor floor =
                Hospital.Instance.m_floors[
                    homeRoom.GetFloorIndex()];
            int bestDistance =
                int.MaxValue;
            bool found =
                false;

            for (int x =
                    homeRoom.m_roomPersistentData.m_positionBottom.m_x;
                x <=
                    homeRoom.m_roomPersistentData.m_positionTop.m_x;
                x++)
            {
                for (int y =
                        homeRoom.m_roomPersistentData.m_positionBottom.m_y;
                    y <=
                        homeRoom.m_roomPersistentData.m_positionTop.m_y;
                    y++)
                {
                    Vector2i candidate =
                        new Vector2i(x, y);
                    if (!homeRoom.IsPositionInRoom(candidate) ||
                        !IsCartPlacementCompletelyFree(
                            cart,
                            floor,
                            candidate))
                    {
                        continue;
                    }

                    Direction candidateOrientation;
                    if (!TryFindAccessibleCartOrientation(
                            cart,
                            floor,
                            homeRoom,
                            candidate,
                            homeOrientation,
                            accessCharacter,
                            rejectFunctionalOverlap,
                            out candidateOrientation))
                    {
                        continue;
                    }

                    int dx =
                        candidate.m_x - homeTile.m_x;
                    int dy =
                        candidate.m_y - homeTile.m_y;
                    int distance =
                        dx * dx + dy * dy;
                    if (distance >= bestDistance)
                    {
                        continue;
                    }

                    bestDistance = distance;
                    placement = candidate;
                    orientation = candidateOrientation;
                    found = true;
                }
            }

            return found;
        }
    }

    [HarmonyPatch(
        typeof(Floor),
        nameof(Floor.Validate),
        new Type[] { })]
    internal static class PorterSampleCartRecoveryPatch
    {
        private static void Prefix(Floor __instance)
        {
            if (MapEditorController.sm_instance != null &&
                MapEditorController.sm_instance.m_unloading)
            {
                return;
            }

            PorterSampleCartRuntime.RecoverOrphanedMovingCarts(
                __instance);
        }
    }


    [HarmonyPatch(
        typeof(BehaviorNurse),
        nameof(BehaviorNurse.GetLookAheadDistance),
        new Type[] { })]
    internal static class PorterSampleCartLookAheadPatch
    {
        private static readonly FieldInfo NurseEntityField =
            AccessTools.Field(
                typeof(BehaviorNurse),
                "m_entity");

        private static void Postfix(
            BehaviorNurse __instance,
            ref int __result)
        {
            Entity porter =
                NurseEntityField == null
                    ? null
                    : NurseEntityField.GetValue(__instance) as Entity;
            if (!PorterIdentity.IsPorter(porter) ||
                !PorterSampleCartRuntime.HasAttachedCart(porter))
            {
                return;
            }

            if (__result < 2)
            {
                __result = 2;
            }
        }
    }

    [HarmonyPatch(
        typeof(BehaviorNurse),
        nameof(BehaviorNurse.GetSpecificAnimation),
        new Type[] { typeof(string) })]
    internal static class PorterSampleCartAnimationPatch
    {
        private static readonly FieldInfo NurseEntityField =
            AccessTools.Field(
                typeof(BehaviorNurse),
                "m_entity");

        private static void Postfix(
            BehaviorNurse __instance,
            string animationID,
            ref string __result)
        {
            Entity porter =
                NurseEntityField == null
                    ? null
                    : NurseEntityField.GetValue(__instance) as Entity;
            if (!PorterIdentity.IsPorter(porter) ||
                animationID != "walk" ||
                !PorterSampleCartRuntime.HasAttachedCart(porter))
            {
                return;
            }

            __result = "walk_push_cart";
        }
    }

    [HarmonyPatch(
        typeof(BehaviorNurse),
        nameof(BehaviorNurse.GetDoorOpeningDistance),
        new Type[] { })]
    internal static class PorterSampleCartDoorOpeningPatch
    {
        private static readonly FieldInfo NurseEntityField =
            AccessTools.Field(
                typeof(BehaviorNurse),
                "m_entity");

        private static void Postfix(
            BehaviorNurse __instance,
            ref int __result)
        {
            Entity porter =
                NurseEntityField == null
                    ? null
                    : NurseEntityField.GetValue(__instance) as Entity;
            if (!PorterIdentity.IsPorter(porter) ||
                !PorterSampleCartRuntime.HasAttachedCart(porter))
            {
                return;
            }

            if (__result < 2)
            {
                __result = 2;
            }
        }
    }

}
