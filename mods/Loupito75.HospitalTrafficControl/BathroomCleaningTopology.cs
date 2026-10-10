using System.Collections.Generic;
using GLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal sealed class BathroomCleaningTopology
    {
        private readonly Room _room;
        private readonly Floor _floor;
        private readonly Dictionary<int, int> _componentByTile =
            new Dictionary<int, int>();
        private readonly Dictionary<TileObject, int> _toiletComponentByObject =
            new Dictionary<TileObject, int>();
        private readonly HashSet<int> _toiletComponents =
            new HashSet<int>();
        private readonly HashSet<int> _occupiedComponents =
            new HashSet<int>();

        private BathroomCleaningTopology(Room room, Floor floor)
        {
            _room = room;
            _floor = floor;
        }

        internal bool HasOccupiedCompartment
        {
            get { return _occupiedComponents.Count > 0; }
        }

        // Diagnostic-only read access to existing topology data.
        internal int DiagnosticToiletCount { get { return _toiletComponentByObject.Count; } }
        internal int DiagnosticToiletCompartmentCount { get { return _toiletComponents.Count; } }
        internal int DiagnosticOccupiedCompartmentCount { get { return _occupiedComponents.Count; } }
        internal int DiagnosticGetTileCompartment(Vector2i tile)
        {
            int component;
            return TryGetComponent(tile, out component) ? component : -1;
        }

        internal bool TryGetToiletCompartment(
            Vector2i tile,
            out int component)
        {
            component = -1;
            return TryGetComponent(tile, out component) &&
                   _toiletComponents.Contains(component);
        }

        internal void GetBathroomFixturesInCompartment(
            int component,
            List<TileObject> fixtures)
        {
            if (fixtures == null ||
                !_toiletComponents.Contains(component))
            {
                return;
            }

            List<TileObject> objects = _room.GetAllObjects(_floor);
            for (int i = 0; i < objects.Count; i++)
            {
                TileObject fixture = objects[i];
                if (fixture == null ||
                    (!fixture.HasTag("wc") &&
                     !fixture.HasTag("washing") &&
                     !fixture.HasTag("dryer")))
                {
                    continue;
                }

                int fixtureComponent;
                if (TryGetFixtureComponent(
                        fixture,
                        out fixtureComponent) &&
                    fixtureComponent == component)
                {
                    fixtures.Add(fixture);
                }
            }
        }

        internal static BathroomCleaningTopology Create(Room room)
        {
            BathroomCleaningTopology topology =
                CreateGeometry(room);
            if (topology == null)
            {
                return null;
            }

            topology.MarkOccupiedPatientComponents();
            topology.MarkOccupiedBladderComponents();
            return topology;
        }

        internal static BathroomCleaningTopology CreateGeometry(
            Room room)
        {
            if (!IsBathroomRoom(room) ||
                Hospital.Instance == null)
            {
                return null;
            }

            int floorIndex = room.GetFloorIndex();
            if (floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return null;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            if (floor == null)
            {
                return null;
            }

            BathroomCleaningTopology topology =
                new BathroomCleaningTopology(room, floor);
            topology.BuildComponents();
            topology.MapToilets();
            return topology;
        }

        internal bool IsTileProtected(Vector2i tile)
        {
            int component;
            return TryGetComponent(tile, out component) &&
                   _toiletComponents.Contains(component) &&
                   _occupiedComponents.Contains(component);
        }

        private static bool IsBathroomRoom(Room room)
        {
            return room != null &&
                   room.m_roomPersistentData != null &&
                   room.m_roomPersistentData.m_roomType.Entry != null &&
                   room.m_roomPersistentData.m_roomType.Entry.HasTag("wc");
        }

        private void BuildComponents()
        {
            int component = 0;

            for (int x = _room.m_roomPersistentData.m_positionBottom.m_x;
                 x <= _room.m_roomPersistentData.m_positionTop.m_x;
                 x++)
            {
                for (int y = _room.m_roomPersistentData.m_positionBottom.m_y;
                     y <= _room.m_roomPersistentData.m_positionTop.m_y;
                     y++)
                {
                    Vector2i seed = new Vector2i(x, y);
                    if (!RoomGeometry.IsInsideRoom(_room, _floor, seed) ||
                        _componentByTile.ContainsKey(TileKey(seed)))
                    {
                        continue;
                    }

                    FloodComponent(seed, component);
                    component++;
                }
            }
        }

        private void FloodComponent(Vector2i seed, int component)
        {
            Queue<Vector2i> pending = new Queue<Vector2i>();
            _componentByTile[TileKey(seed)] = component;
            pending.Enqueue(seed);

            while (pending.Count > 0)
            {
                Vector2i current = pending.Dequeue();

                TryEnqueue(
                    current,
                    new Vector2i(current.m_x - 1, current.m_y),
                    component,
                    pending);
                TryEnqueue(
                    current,
                    new Vector2i(current.m_x + 1, current.m_y),
                    component,
                    pending);
                TryEnqueue(
                    current,
                    new Vector2i(current.m_x, current.m_y - 1),
                    component,
                    pending);
                TryEnqueue(
                    current,
                    new Vector2i(current.m_x, current.m_y + 1),
                    component,
                    pending);
            }
        }

        private void TryEnqueue(
            Vector2i current,
            Vector2i next,
            int component,
            Queue<Vector2i> pending)
        {
            if (!RoomGeometry.IsInsideRoom(_room, _floor, next))
            {
                return;
            }

            int key = TileKey(next);
            if (_componentByTile.ContainsKey(key) ||
                !RoomGeometry.IsConnectedInsideRoomWithoutWall(
                    _room,
                    _floor,
                    current,
                    next))
            {
                return;
            }

            _componentByTile[key] = component;
            pending.Enqueue(next);
        }

        private void MapToilets()
        {
            List<TileObject> objects = _room.GetAllObjects(_floor);
            for (int i = 0; i < objects.Count; i++)
            {
                TileObject toilet = objects[i];
                if (toilet == null ||
                    toilet.m_state == null ||
                    !toilet.HasTag("wc"))
                {
                    continue;
                }

                int component;
                if (!TryGetFixtureComponent(toilet, out component))
                {
                    continue;
                }

                _toiletComponentByObject[toilet] = component;
                _toiletComponents.Add(component);
            }
        }

        private bool TryGetFixtureComponent(
            TileObject fixture,
            out int component)
        {
            component = -1;
            if (fixture == null || fixture.m_state == null)
            {
                return false;
            }

            Vector2f usePosition = fixture.GetDefaultUsePosition();
            Vector2i useTile = ToTile(usePosition);
            if (TryGetComponent(useTile, out component))
            {
                return true;
            }

            return TryGetComponent(
                fixture.m_state.m_position,
                out component);
        }

        private void MarkOccupiedPatientComponents()
        {
            if (Hospital.Instance == null ||
                Hospital.Instance.m_departments == null)
            {
                return;
            }

            foreach (Department department in Hospital.Instance.m_departments)
            {
                if (department == null ||
                    department.m_departmentPersistentData == null ||
                    department.m_departmentPersistentData.m_patients == null)
                {
                    continue;
                }

                foreach (EntityIDPointer<Entity> pointer in
                         department.m_departmentPersistentData.m_patients)
                {
                    Entity patient =
                        pointer == null
                            ? null
                            : pointer.GetEntity();

                    if (patient == null ||
                        patient.GetComponent<BehaviorPatient>() == null)
                    {
                        continue;
                    }

                    WalkComponent walk = patient.GetComponent<WalkComponent>();
                    if (walk == null ||
                        walk.GetFloorIndex() != _floor.m_floorIndex)
                    {
                        continue;
                    }

                    int component;
                    if (TryGetComponent(
                            walk.GetCurrentTile(),
                            out component) &&
                        _toiletComponents.Contains(component))
                    {
                        _occupiedComponents.Add(component);
                    }
                }
            }
        }

        private void MarkOccupiedBladderComponents()
        {
            ProcedureManager manager = ProcedureManager.GetInstance();
            if (manager == null || manager.m_scriptEntities == null)
            {
                return;
            }

            foreach (ProcedureScript script in manager.m_scriptEntities)
            {
                ProcedureScriptNeedBladder bladder =
                    script as ProcedureScriptNeedBladder;

                if (bladder == null ||
                    bladder.m_stateData == null ||
                    bladder.m_stateData.m_procedureScene == null)
                {
                    continue;
                }

                TileObject toilet = bladder.GetEquipment(0);
                int toiletComponent;
                if (toilet == null ||
                    !_toiletComponentByObject.TryGetValue(
                        toilet,
                        out toiletComponent))
                {
                    continue;
                }

                string state = bladder.m_stateData.m_state;
                if (state == ProcedureScriptNeedBladder.STATE_GOING_TO_OBJECT ||
                    state == ProcedureScriptNeedBladder.STATE_USING_OBJECT)
                {
                    _occupiedComponents.Add(toiletComponent);
                    continue;
                }

                Entity character =
                    bladder.m_stateData.m_procedureScene.MainCharacter;
                WalkComponent walk =
                    character == null
                        ? null
                        : character.GetComponent<WalkComponent>();

                if (walk == null ||
                    walk.GetFloorIndex() != _floor.m_floorIndex)
                {
                    continue;
                }

                int characterComponent;
                if (TryGetComponent(
                        walk.GetCurrentTile(),
                        out characterComponent) &&
                    characterComponent == toiletComponent)
                {
                    _occupiedComponents.Add(toiletComponent);
                }
            }
        }

        private bool TryGetComponent(Vector2i tile, out int component)
        {
            component = -1;
            if (!RoomGeometry.IsInsideRoom(_room, _floor, tile))
            {
                return false;
            }

            return _componentByTile.TryGetValue(
                TileKey(tile),
                out component);
        }

        private int TileKey(Vector2i tile)
        {
            return tile.m_y * _floor.Size.m_x + tile.m_x;
        }

        private static Vector2i ToTile(Vector2f position)
        {
            return new Vector2i(
                (int)(position.m_x + 0.5f),
                (int)(position.m_y + 0.5f));
        }
    }
}
