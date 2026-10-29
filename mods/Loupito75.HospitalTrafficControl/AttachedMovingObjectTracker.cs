using System.Collections.Generic;
using GLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal static class AttachedMovingObjectTracker
    {
        private static readonly Dictionary<TileObject, Entity> OwnersByObject =
            new Dictionary<TileObject, Entity>();

        private static readonly Dictionary<Entity, int> CountsByOwner =
            new Dictionary<Entity, int>();

        private static bool s_initialized;

        internal static bool HasAttachedObject(Entity character)
        {
            if (character == null)
            {
                return false;
            }

            EnsureInitialized();

            int count;
            return CountsByOwner.TryGetValue(character, out count) &&
                   count > 0;
        }

        internal static void Refresh(TileObject tileObject)
        {
            if (!s_initialized || tileObject == null)
            {
                return;
            }

            Entity previousOwner;
            OwnersByObject.TryGetValue(tileObject, out previousOwner);

            Entity currentOwner =
                tileObject.IsAttachedToCharacter()
                    ? tileObject.User
                    : null;

            if (object.ReferenceEquals(previousOwner, currentOwner))
            {
                return;
            }

            if (previousOwner != null)
            {
                RemoveOwner(previousOwner);
                OwnersByObject.Remove(tileObject);
            }

            if (currentOwner != null)
            {
                OwnersByObject[tileObject] = currentOwner;
                AddOwner(currentOwner);
            }
        }

        internal static void Forget(TileObject tileObject)
        {
            if (!s_initialized || tileObject == null)
            {
                return;
            }

            Entity owner;
            if (!OwnersByObject.TryGetValue(tileObject, out owner))
            {
                return;
            }

            OwnersByObject.Remove(tileObject);
            RemoveOwner(owner);
        }

        internal static void Invalidate()
        {
            OwnersByObject.Clear();
            CountsByOwner.Clear();
            s_initialized = false;
        }

        internal static void Reset()
        {
            Invalidate();
        }

        private static void EnsureInitialized()
        {
            if (s_initialized)
            {
                return;
            }

            OwnersByObject.Clear();
            CountsByOwner.Clear();

            Hospital hospital = Hospital.Instance;
            if (hospital != null && hospital.m_floors != null)
            {
                for (int floorIndex = 0;
                     floorIndex < hospital.m_floors.Count;
                     floorIndex++)
                {
                    Floor floor = hospital.m_floors[floorIndex];
                    if (floor == null || floor.m_movingObjects == null)
                    {
                        continue;
                    }

                    foreach (TileObject tileObject in floor.m_movingObjects)
                    {
                        if (tileObject == null ||
                            !tileObject.IsAttachedToCharacter())
                        {
                            continue;
                        }

                        Entity owner = tileObject.User;
                        if (owner == null)
                        {
                            continue;
                        }

                        OwnersByObject[tileObject] = owner;
                        AddOwner(owner);
                    }
                }
            }

            s_initialized = true;
        }

        private static void AddOwner(Entity owner)
        {
            int count;
            if (CountsByOwner.TryGetValue(owner, out count))
            {
                CountsByOwner[owner] = count + 1;
            }
            else
            {
                CountsByOwner[owner] = 1;
            }
        }

        private static void RemoveOwner(Entity owner)
        {
            if (owner == null)
            {
                return;
            }

            int count;
            if (!CountsByOwner.TryGetValue(owner, out count))
            {
                return;
            }

            if (count <= 1)
            {
                CountsByOwner.Remove(owner);
            }
            else
            {
                CountsByOwner[owner] = count - 1;
            }
        }
    }
}
