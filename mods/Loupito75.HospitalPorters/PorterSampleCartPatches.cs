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
            TileObject cart)
        {
            if (porter == null ||
                cart == null ||
                cart.User != porter ||
                cart.m_state.m_moving ||
                MapScriptInterface.Instance == null)
            {
                return false;
            }

            if (!MapScriptInterface.Instance.PickUpObject(cart))
            {
                return false;
            }

            cart.SetAttachedToCharacter(attached: true);
            return cart.m_state.m_moving &&
                cart.IsAttachedToCharacter() &&
                cart.User == porter;
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

            parkingTile =
                MapScriptInterface.Instance.FindClosest3x3Area(
                    interactionObject.m_state.m_position,
                    interactionRoom);
            if (parkingTile != Vector2i.ZERO_VECTOR)
            {
                return true;
            }

            return TryFindFallbackParkingTile(
                cart,
                interactionObject,
                interactionRoom,
                out parkingTile);
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
                MapScriptInterface.Instance == null)
            {
                return false;
            }

            Vector2i parkingTile =
                walk.GetCurrentTile();

            cart.StopSounds();
            if (!MapScriptInterface.Instance.MoveObject(
                    cart,
                    parkingTile))
            {
                return false;
            }

            cart.SetAttachedToCharacter(attached: false);
            cart.m_state.m_originalFloorIndex =
                job.CartHomeFloorIndex;


            return !cart.m_state.m_moving &&
                !cart.IsAttachedToCharacter() &&
                cart.User == porter;
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

        private static bool TryFindFallbackParkingTile(
            TileObject cart,
            TileObject interactionObject,
            Room interactionRoom,
            out Vector2i parkingTile)
        {
            parkingTile = Vector2i.ZERO_VECTOR;
            if (cart == null ||
                interactionObject == null ||
                interactionRoom == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
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
            Vector2i interactionPosition =
                interactionObject.m_state.m_position;
            Vector2f nativeUsePosition =
                interactionObject.GetDefaultUsePosition();
            Vector2i usePosition =
                new Vector2i(
                    (int)(nativeUsePosition.m_x + 0.5f),
                    (int)(nativeUsePosition.m_y + 0.5f));
            int bestDistance =
                int.MaxValue;

            for (int x =
                    interactionRoom.m_roomPersistentData.m_positionBottom.m_x;
                x <=
                    interactionRoom.m_roomPersistentData.m_positionTop.m_x;
                x++)
            {
                for (int y =
                        interactionRoom.m_roomPersistentData.m_positionBottom.m_y;
                    y <=
                        interactionRoom.m_roomPersistentData.m_positionTop.m_y;
                    y++)
                {
                    Vector2i candidate =
                        new Vector2i(x, y);
                    if (!interactionRoom.IsPositionInRoom(candidate) ||
                        candidate == interactionPosition ||
                        candidate == usePosition ||
                        x < 0 ||
                        y < 0 ||
                        x >= floor.Size.m_x ||
                        y >= floor.Size.m_y)
                    {
                        continue;
                    }

                    if (floor.m_mapPersistentData.m_tiles[x, y].m_user != null ||
                        floor.m_mapPersistentData.m_foundationsLayer.m_foundations[x, y] != 2 ||
                        floor.m_accessibility[x, y] == 2 ||
                        floor.m_tileObjects[x, y].m_centerObject != null ||
                        floor.m_tileObjects[x, y].IsAnyObjectBlocking())
                    {
                        continue;
                    }

                    int dx =
                        x - interactionPosition.m_x;
                    int dy =
                        y - interactionPosition.m_y;
                    int distance =
                        dx * dx + dy * dy;
                    if (distance >= bestDistance)
                    {
                        continue;
                    }

                    bestDistance = distance;
                    parkingTile = candidate;
                }
            }

            return parkingTile != Vector2i.ZERO_VECTOR;
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
            if (!cart.m_state.m_moving)
            {
                bool alreadyHome =
                    cart.GetFloorIndex() ==
                        job.CartHomeFloorIndex &&
                    cart.m_state.m_position ==
                        job.CartHomeTile;

                if (alreadyHome)
                {
                    cart.Orientation =
                        job.CartHomeOrientation;
                    if (cart.User == porter)
                    {
                        cart.User = null;
                    }
                    cart.SetAttachedToCharacter(
                        attached: false);
                    cart.m_state.m_originalFloorIndex =
                        -1;
                    return true;
                }

                if (MapScriptInterface.Instance == null ||
                    !MapScriptInterface.Instance.PickUpObject(
                        cart))
                {
                    return false;
                }

                cart.SetAttachedToCharacter(
                    attached: false);
                cart.m_state.m_originalFloorIndex =
                    job.CartHomeFloorIndex;
            }

            return RestoreMovingCart(
                cart,
                job.CartHomeRoom,
                job.CartHomeTile,
                job.CartHomeFloorIndex,
                job.CartHomeOrientation);
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
                                cart.Orientation);

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

                RecoverOrphanedMovingCart(cart);
            }
        }

        internal static int CountTotalCartsForStation(
            Room station)
        {
            if (station == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
            {
                return 0;
            }

            int floorIndex = station.GetFloorIndex();
            if (floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return 0;
            }

            int fixedCarts =
                station.GetObjectCountWithTag(
                    PorterIds.SampleCartTag,
                    Hospital.Instance.m_floors[floorIndex]);
            return fixedCarts +
                PorterSampleTransportRuntime.CountActiveCartsAwayFromStation(
                    station) +
                CountMovingCartsForStation(station);
        }

        internal static int CountMovingCartsForStation(
            Room station)
        {
            if (station == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
            {
                return 0;
            }

            int count = 0;
            int stationFloor = station.GetFloorIndex();

            foreach (Floor floor in Hospital.Instance.m_floors)
            {
                if (floor == null || floor.m_movingObjects == null)
                {
                    continue;
                }

                for (int i = 0; i < floor.m_movingObjects.Count; i++)
                {
                    TileObject cart = floor.m_movingObjects[i];
                    if (!IsSampleCart(cart) ||
                        PorterSampleTransportRuntime.IsCartInActiveJob(
                            cart) ||
                        cart.m_state.m_originalFloorIndex != stationFloor ||
                        !station.IsPositionInRoom(cart.m_state.m_position))
                    {
                        continue;
                    }

                    count++;
                }
            }

            return count;
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

        private static TileObject FindFreeCart(
            Entity porter,
            out Room homeRoom)
        {
            homeRoom = null;
            if (porter == null ||
                MapScriptInterface.Instance == null)
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

            TileObject cart =
                MapScriptInterface.Instance.FindClosestFreeObjectWithTag(
                    porter,
                    null,
                    walk.GetCurrentTile(),
                    homeRoom,
                    PorterIds.SampleCartTag,
                    AccessRights.STAFF);

            if (cart == null ||
                cart.User != null ||
                cart.Owner != null ||
                cart.m_state.m_moving ||
                !cart.IsValid())
            {
                return null;
            }

            return cart;
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
                MapScriptInterface.Instance == null)
            {
                return;
            }

            int homeFloorIndex =
                cart.m_state.m_originalFloorIndex;
            if (homeFloorIndex < 0 ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null ||
                homeFloorIndex >= Hospital.Instance.m_floors.Count)
            {
                Plugin.Log?.LogWarning(
                    "Could not recover orphaned Porter sample cart because its original floor is unavailable.");
                return;
            }

            Vector2i homeTile =
                cart.m_state.m_position;
            Room homeRoom =
                MapScriptInterface.Instance.GetRoomAt(
                    homeTile,
                    homeFloorIndex);
            Direction homeOrientation =
                cart.Orientation;

            bool restored =
                RestoreMovingCart(
                    cart,
                    homeRoom,
                    homeTile,
                    homeFloorIndex,
                    homeOrientation);

            PorterDiagnostics.Log(
                "cart recovery: movingCartRestored=" +
                restored +
                "; homeFloor=" +
                homeFloorIndex +
                ".");
        }

        private static bool RestoreMovingCart(
            TileObject cart,
            Room homeRoom,
            Vector2i homeTile,
            int homeFloorIndex,
            Direction homeOrientation)
        {
            if (!IsSampleCart(cart) ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null ||
                homeFloorIndex < 0 ||
                homeFloorIndex >= Hospital.Instance.m_floors.Count ||
                MapScriptInterface.Instance == null)
            {
                return false;
            }

            cart.User = null;
            cart.SetAttachedToCharacter(attached: false);
            cart.StopSounds();

            MoveCartToFloor(cart, homeFloorIndex);

            Vector2i placement = homeTile;
            bool canPlace =
                homeRoom != null &&
                homeRoom.GetFloorIndex() == homeFloorIndex &&
                homeRoom.IsPositionInRoom(homeTile) &&
                MapScriptInterface.Instance.CanMoveObject(
                    cart,
                    homeTile);

            if (!canPlace &&
                homeRoom != null &&
                homeRoom.GetFloorIndex() == homeFloorIndex)
            {
                canPlace =
                    TryFindFallbackPlacement(
                        cart,
                        homeRoom,
                        out placement);
            }

            if (!canPlace ||
                !MapScriptInterface.Instance.MoveObject(
                    cart,
                    placement))
            {
                Plugin.Log?.LogWarning(
                    "Porter sample cart could not be placed back in its station; it remains a recoverable moving object.");
                return false;
            }

            cart.Orientation = homeOrientation;
            cart.m_state.m_originalFloorIndex = -1;
            cart.StopSounds();
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
            out Vector2i placement)
        {
            placement = Vector2i.ZERO_VECTOR;
            if (cart == null || homeRoom == null)
            {
                return false;
            }

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
                        !MapScriptInterface.Instance.CanMoveObject(
                            cart,
                            candidate))
                    {
                        continue;
                    }

                    placement = candidate;
                    return true;
                }
            }

            return false;
        }
    }

    [HarmonyPatch(
        typeof(Room),
        nameof(Room.GetObjectCountWithTag),
        new Type[]
        {
            typeof(string),
            typeof(Floor),
            typeof(bool)
        })]
    internal static class PorterSampleCartRoomCountPatch
    {
        private static void Postfix(
            Room __instance,
            string tag,
            ref int __result)
        {
            if (tag != PorterIds.SampleCartUiTag ||
                __instance == null ||
                !PorterStationRegistry.IsPorterStation(
                    __instance.m_roomPersistentData.m_roomType.Entry))
            {
                return;
            }

            __result +=
                PorterSampleTransportRuntime.CountActiveCartsAwayFromStation(
                    __instance) +
                PorterSampleCartRuntime.CountMovingCartsForStation(
                    __instance);
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
    [HarmonyPatch(
        typeof(ObjectRenderer),
        nameof(ObjectRenderer.RenderObjects),
        new Type[]
        {
            typeof(bool), typeof(float), typeof(Floor),
            typeof(LightMap2DUnity), typeof(FloorRenderData)
        })]
    internal static class PorterSampleCartBuildingGhostPatch
    {
        private static readonly MethodInfo RenderSingleObjectMethod =
            AccessTools.Method(
                typeof(ObjectRenderer),
                "RenderSingleObject",
                new Type[]
                {
                    typeof(TileObject), typeof(float), typeof(float),
                    typeof(Floor), typeof(LightMap2DUnity),
                    typeof(bool), typeof(bool)
                });

        private static bool s_loggedRenderFailure;

        private static void Postfix(
            ObjectRenderer __instance,
            float zoomScale,
            Floor floor,
            LightMap2DUnity lightMap)
        {
            if (__instance == null ||
                floor == null ||
                ViewModeController.Instance == null)
            {
                return;
            }

            if ((ViewModeController.Instance.m_currentMode !=
                    ViewModes.BUILDING &&
                 ViewModeController.Instance.m_currentMode !=
                    ViewModes.BUILDING_FLOORS) ||
                RenderSingleObjectMethod == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
            {
                return;
            }

            foreach (Floor sourceFloor in Hospital.Instance.m_floors)
            {
                if (sourceFloor == null ||
                    sourceFloor.m_movingObjects == null)
                {
                    continue;
                }

                foreach (TileObject cart in sourceFloor.m_movingObjects)
                {
                    if (!PorterSampleCartRuntime.IsSampleCart(cart) ||
                        !cart.m_state.m_moving ||
                        cart.m_state.m_originalFloorIndex !=
                            floor.m_floorIndex)
                    {
                        continue;
                    }

                    try
                    {
                        RenderSingleObjectMethod.Invoke(
                            __instance,
                            new object[]
                            {
                                cart,
                                -0.25f,
                                zoomScale,
                                floor,
                                lightMap,
                                false,
                                true
                            });
                    }
                    catch (Exception exception)
                    {
                        if (!s_loggedRenderFailure)
                        {
                            s_loggedRenderFailure = true;
                            Plugin.Log?.LogError(
                                "Could not render the native-style sample-cart building ghost: " +
                                exception);
                        }
                        return;
                    }
                }
            }
        }

    }

}
