using System;
using System.Collections.Generic;
using System.Globalization;
using GLib;
using Lopital;

namespace HospitalFixes
{
    internal static class HfxDiagnostics
    {
        private sealed class RateState
        {
            internal long LastTicks;
            internal int Suppressed;
        }

        private static bool _enabled;
        private static object _sync;
        private static Dictionary<string, int> _counts;
        private static Dictionary<string, RateState> _rateStates;

        internal static bool Enabled
        {
            get { return _enabled; }
        }

        internal static void StartSession()
        {

            _enabled =
                FixConfig.EnableDiagnostics &&
                Plugin.Log != null;

            if (!_enabled)
            {
                return;
            }


            _sync = new object();
            _counts =
                new Dictionary<string, int>(StringComparer.Ordinal);
            _rateStates =
                new Dictionary<string, RateState>(StringComparer.Ordinal);

            Plugin.Log.LogInfo(
                "[Diagnostics] enabled=true version=" +
                Plugin.PluginVersion +
                " mode=observed-intervention-result rate-limited");
        }

        internal static string EntityLabel(Entity entity)
        {
            if (entity == null)
            {
                return "none";
            }

            string name = entity.Name == null
                ? string.Empty
                : entity.Name.Trim();

            return name.Length == 0
                ? entity.GetType().Name
                : name + " (" + entity.GetType().Name + ")";
        }

        internal static string EntityContext(Entity entity)
        {
            if (entity == null)
            {
                return "none";
            }

            WalkComponent walk =
                entity.GetComponent<WalkComponent>();
            string position =
                walk == null ||
                walk.m_state == null
                    ? "unknown"
                    : VectorLabel(
                        walk.GetCurrentTile(),
                        walk.GetFloorIndex());

            Room room = null;
            if (walk != null &&
                MapScriptInterface.Instance != null)
            {
                try
                {
                    room =
                        MapScriptInterface.Instance
                            .GetRoomAt(walk);
                }
                catch
                {
                    room = null;
                }
            }

            return EntityLabel(entity) +
                " position=" + position +
                " department=" +
                EntityDepartmentLabel(entity) +
                " room=" +
                RoomShortLabel(room);
        }

        internal static string EntityDepartmentLabel(
            Entity entity)
        {
            if (entity == null)
            {
                return "none";
            }

            EmployeeComponent employee =
                entity.GetComponent<EmployeeComponent>();
            if (employee != null &&
                employee.m_state != null &&
                employee.m_state.m_department != null &&
                employee.m_state.m_department.CheckEntity())
            {
                return DepartmentLabel(
                    employee.m_state.m_department
                        .GetEntity());
            }

            BehaviorPatient patient =
                entity.GetComponent<BehaviorPatient>();
            if (patient != null &&
                patient.m_state != null &&
                patient.m_state.m_department != null &&
                patient.m_state.m_department.CheckEntity())
            {
                return DepartmentLabel(
                    patient.m_state.m_department
                        .GetEntity());
            }

            return "none";
        }

        internal static string DepartmentLabel(
            Department department)
        {
            if (department == null ||
                department.m_departmentPersistentData == null ||
                department.m_departmentPersistentData
                    .m_departmentType == null ||
                department.m_departmentPersistentData
                    .m_departmentType.Entry == null ||
                ID.IsNullOrNoID(
                    department.m_departmentPersistentData
                        .m_departmentType.Entry.DatabaseID))
            {
                return "none";
            }

            return department.m_departmentPersistentData
                .m_departmentType.Entry.DatabaseID.ToString();
        }

        internal static string RoomContext(Room room)
        {
            if (room == null ||
                room.m_roomPersistentData == null)
            {
                return "none";
            }

            RoomPersistentData state =
                room.m_roomPersistentData;
            string roomType =
                state.m_roomType != null &&
                state.m_roomType.Entry != null &&
                !ID.IsNullOrNoID(
                    state.m_roomType.Entry.DatabaseID)
                    ? state.m_roomType.Entry
                        .DatabaseID.ToString()
                    : "unknown";

            Department department =
                state.m_department != null &&
                state.m_department.CheckEntity()
                    ? state.m_department.GetEntity()
                    : null;

            return roomType +
                " floor=" +
                room.GetFloorIndex()
                    .ToString(CultureInfo.InvariantCulture) +
                " bounds=" +
                state.m_positionTop.m_x + "," +
                state.m_positionTop.m_y + "-" +
                state.m_positionBottom.m_x + "," +
                state.m_positionBottom.m_y +
                " department=" +
                DepartmentLabel(department);
        }

        internal static string ObjectContext(
            TileObject tileObject)
        {
            if (tileObject == null ||
                tileObject.m_state == null)
            {
                return "none";
            }

            Department department =
                tileObject.m_state.m_department != null &&
                tileObject.m_state.m_department.CheckEntity()
                    ? tileObject.m_state.m_department
                        .GetEntity()
                    : null;

            return ObjectLabel(tileObject) +
                " department=" +
                DepartmentLabel(department);
        }

        private static string RoomShortLabel(Room room)
        {
            if (room == null ||
                room.m_roomPersistentData == null)
            {
                return "none";
            }

            RoomPersistentData state =
                room.m_roomPersistentData;
            string roomType =
                state.m_roomType != null &&
                state.m_roomType.Entry != null &&
                !ID.IsNullOrNoID(
                    state.m_roomType.Entry.DatabaseID)
                    ? state.m_roomType.Entry
                        .DatabaseID.ToString()
                    : "unknown";

            Department department =
                state.m_department != null &&
                state.m_department.CheckEntity()
                    ? state.m_department.GetEntity()
                    : null;

            return roomType +
                "@floor" +
                room.GetFloorIndex()
                    .ToString(CultureInfo.InvariantCulture) +
                "/department=" +
                DepartmentLabel(department);
        }

        internal static void Observed(
            string fix,
            string source,
            string outcome,
            string details)
        {
            Record(
                "observed",
                fix,
                source,
                outcome,
                details,
                null,
                0);
        }

        internal static void Intervention(
            string fix,
            string source,
            string outcome,
            string details)
        {
            Record(
                "intervention",
                fix,
                source,
                outcome,
                details,
                null,
                0);
        }

        internal static void Result(
            string fix,
            string source,
            string outcome,
            string details)
        {
            Record(
                "result",
                fix,
                source,
                outcome,
                details,
                null,
                0);
        }

        internal static void Fallback(
            string fix,
            string source,
            string outcome,
            string details)
        {
            Record(
                "fallback",
                fix,
                source,
                outcome,
                details,
                null,
                0);
        }

        internal static void ObservedRateLimited(
            string fix,
            string source,
            string outcome,
            string details,
            string repeatKey,
            int intervalMilliseconds)
        {
            Record(
                "observed",
                fix,
                source,
                outcome,
                details,
                repeatKey,
                intervalMilliseconds);
        }

        internal static void InterventionRateLimited(
            string fix,
            string source,
            string outcome,
            string details,
            string repeatKey,
            int intervalMilliseconds)
        {
            Record(
                "intervention",
                fix,
                source,
                outcome,
                details,
                repeatKey,
                intervalMilliseconds);
        }

        internal static void ResultRateLimited(
            string fix,
            string source,
            string outcome,
            string details,
            string repeatKey,
            int intervalMilliseconds)
        {
            Record(
                "result",
                fix,
                source,
                outcome,
                details,
                repeatKey,
                intervalMilliseconds);
        }

        // Compatibility entry point for already-instrumented fixes. The kind is
        // inferred conservatively from the outcome until the call site needs a
        // more explicit observed/intervention/result/fallback distinction.
        internal static void Event(
            string fix,
            string source,
            string outcome,
            string details)
        {
            Record(
                ClassifyOutcome(outcome),
                fix,
                source,
                outcome,
                details,
                null,
                0);
        }

        internal static void EventRateLimited(
            string fix,
            string source,
            string outcome,
            string details,
            string repeatKey,
            int intervalMilliseconds)
        {
            Record(
                ClassifyOutcome(outcome),
                fix,
                source,
                outcome,
                details,
                repeatKey,
                intervalMilliseconds);
        }

        internal static void ReportHospitalizedProcedureEquipmentSelection(
            Entity character,
            string[] tags,
            TileObject result,
            float distance,
            Vector2i from,
            int fromFloor,
            Vector2i transportTarget,
            int transportTargetFloor,
            bool usesTransportTarget,
            bool sawNegative,
            bool sawReachable)
        {
            if (!_enabled ||
                result == null)
            {
                return;
            }

            const string fix =
                "FixHospitalizedProcedureEquipmentSelection";
            const string source =
                "MapScriptInterface.FindClosestFreeObjectWithTags";
            const int intervalMilliseconds = 2000;

            string tagsLabel =
                TagsLabel(tags);
            string resultLabel =
                ObjectLabel(result);

            if (usesTransportTarget)
            {
                InterventionRateLimited(
                    fix,
                    source,
                    "transport-target-scoring-selected",
                    "patient=" +
                    EntityLabel(character) +
                    " tags=" +
                    tagsLabel +
                    " selected=" +
                    resultLabel +
                    " distance=" +
                    distance.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture) +
                    " from=" +
                    VectorLabel(
                        from,
                        fromFloor) +
                    " transportTarget=" +
                    VectorLabel(
                        transportTarget,
                        transportTargetFloor) +
                    " negativeFallbackSeen=" +
                    sawNegative,
                    fix +
                    "|transport|" +
                    tagsLabel +
                    "|" +
                    resultLabel,
                    intervalMilliseconds);
            }

            if (sawNegative &&
                sawReachable)
            {
                InterventionRateLimited(
                    fix,
                    source,
                    "reachable-candidate-preferred-over-negative-fallback",
                    "patient=" +
                    EntityLabel(character) +
                    " tags=" +
                    tagsLabel +
                    " selected=" +
                    resultLabel +
                    " distance=" +
                    distance.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture),
                    fix +
                    "|reachable-over-negative|" +
                    tagsLabel,
                    intervalMilliseconds);
            }
        }

        internal static void ReportDuplicateEmployeeDayStartSuppressed(
            Floor floor)
        {
            if (!_enabled ||
                floor == null)
            {
                return;
            }

            int day =
                DayTime.Instance == null
                    ? -1
                    : DayTime.Instance.GetDay();
            int floorCount =
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null
                    ? -1
                    : Hospital.Instance.m_floors.Count;

            InterventionRateLimited(
                "PreventDuplicateEmployeeDayStart",
                "Floor.OnDayStart",
                "duplicate-floor-pass-suppressed",
                "day=" +
                day.ToString(CultureInfo.InvariantCulture) +
                " floor=" +
                floor.m_floorIndex.ToString(CultureInfo.InvariantCulture) +
                " floors=" +
                floorCount.ToString(CultureInfo.InvariantCulture) +
                " employeeLoop=Hospital.Instance.m_characters" +
                " hfxRandomRangeCalls=0",
                "PreventDuplicateEmployeeDayStart|day|" +
                day.ToString(CultureInfo.InvariantCulture),
                1000);
        }

        internal static void ReportEmployeeDayStartResult(
            Floor floor)
        {
            if (!_enabled ||
                floor == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_characters == null)
            {
                return;
            }

            int employees = 0;
            int alcoholismDayEmployees = 0;
            int alcoholTriggers = 0;
            int efficiencyOne = 0;
            int efficiencyHalf = 0;
            int invalidEfficiency = 0;
            int invalidCommute = 0;
            int triggerWithoutEligibleAlcoholism = 0;
            int hungoverMissingAfterTrigger = 0;
            int inspectionErrors = 0;

            foreach (Entity character in Hospital.Instance.m_characters)
            {
                if (character == null)
                {
                    continue;
                }

                EmployeeComponent employee =
                    character.GetComponent<EmployeeComponent>();
                if (employee == null ||
                    employee.m_state == null)
                {
                    continue;
                }

                employees++;

                try
                {
                    PerkComponent perkComponent =
                        character.GetComponent<PerkComponent>();
                    bool longCommute =
                        perkComponent != null &&
                        perkComponent.m_perkSet != null &&
                        perkComponent.m_perkSet.HasPerk(
                            "PERK_LONG_COMMUTE");
                    bool alcoholismDay =
                        perkComponent != null &&
                        perkComponent.m_perkSet != null &&
                        perkComponent.m_perkSet.HasPerk(
                            "PERK_ALCOHOLISM") &&
                        employee.m_state.m_shift == Shift.DAY;

                    if (alcoholismDay)
                    {
                        alcoholismDayEmployees++;
                    }

                    float efficiency =
                        employee.m_state.m_efficiency;
                    bool alcoholTriggered =
                        Math.Abs(efficiency - 0.5f) < 0.0001f;

                    if (Math.Abs(efficiency - 1f) < 0.0001f)
                    {
                        efficiencyOne++;
                    }
                    else if (alcoholTriggered)
                    {
                        efficiencyHalf++;
                        alcoholTriggers++;
                    }
                    else
                    {
                        invalidEfficiency++;
                    }

                    if (alcoholTriggered &&
                        !alcoholismDay)
                    {
                        triggerWithoutEligibleAlcoholism++;
                    }

                    if (!IsExpectedEmployeeCommute(
                            employee,
                            longCommute,
                            alcoholTriggered))
                    {
                        invalidCommute++;
                    }

                    if (alcoholTriggered)
                    {
                        MoodComponent mood =
                            character.GetComponent<MoodComponent>();
                        if (mood == null ||
                            !mood.HasSatisfactionModifier(
                                "SAT_MOD_HUNGOVER"))
                        {
                            hungoverMissingAfterTrigger++;
                        }
                    }
                }
                catch
                {
                    inspectionErrors++;
                }
            }

            int floorCount =
                Hospital.Instance.m_floors == null
                    ? -1
                    : Hospital.Instance.m_floors.Count;
            int inferredVanillaRngDraws =
                employees +
                alcoholismDayEmployees +
                alcoholTriggers;
            int day =
                DayTime.Instance == null
                    ? -1
                    : DayTime.Instance.GetDay();

            string details =
                "day=" +
                day.ToString(CultureInfo.InvariantCulture) +
                " floors=" +
                floorCount.ToString(CultureInfo.InvariantCulture) +
                " suppressedFloorPasses=" +
                Math.Max(0, floorCount - 1).ToString(
                    CultureInfo.InvariantCulture) +
                " employees=" +
                employees.ToString(CultureInfo.InvariantCulture) +
                " alcoholismDayEmployees=" +
                alcoholismDayEmployees.ToString(
                    CultureInfo.InvariantCulture) +
                " alcoholTriggers=" +
                alcoholTriggers.ToString(
                    CultureInfo.InvariantCulture) +
                " inferredVanillaRngDraws=" +
                inferredVanillaRngDraws.ToString(
                    CultureInfo.InvariantCulture) +
                " hfxRandomRangeCalls=0" +
                " efficiencyOne=" +
                efficiencyOne.ToString(CultureInfo.InvariantCulture) +
                " efficiencyHalf=" +
                efficiencyHalf.ToString(CultureInfo.InvariantCulture) +
                " invalidEfficiency=" +
                invalidEfficiency.ToString(
                    CultureInfo.InvariantCulture) +
                " invalidCommute=" +
                invalidCommute.ToString(CultureInfo.InvariantCulture) +
                " triggerWithoutEligibleAlcoholism=" +
                triggerWithoutEligibleAlcoholism.ToString(
                    CultureInfo.InvariantCulture) +
                " hungoverMissingAfterTrigger=" +
                hungoverMissingAfterTrigger.ToString(
                    CultureInfo.InvariantCulture) +
                " inspectionErrors=" +
                inspectionErrors.ToString(
                    CultureInfo.InvariantCulture);

            if (invalidEfficiency == 0 &&
                invalidCommute == 0 &&
                triggerWithoutEligibleAlcoholism == 0 &&
                hungoverMissingAfterTrigger == 0 &&
                inspectionErrors == 0)
            {
                Result(
                    "PreventDuplicateEmployeeDayStart",
                    "EmployeeComponent.OnDayStart via Floor.OnDayStart",
                    "single-daily-pass-sane",
                    details);
            }
            else
            {
                Fallback(
                    "PreventDuplicateEmployeeDayStart",
                    "EmployeeComponent.OnDayStart via Floor.OnDayStart",
                    "post-day-start-invariant-warning",
                    details);
            }
        }

        private static bool IsExpectedEmployeeCommute(
            EmployeeComponent employee,
            bool longCommute,
            bool alcoholTriggered)
        {
            float commute =
                employee.m_state.m_commuteTime;

            if (alcoholTriggered)
            {
                return commute >= 2f &&
                    commute <= 4f;
            }

            if (longCommute)
            {
                return commute >= 0.25f &&
                    commute <= 2f;
            }

            Room homeRoom =
                employee.m_state.m_homeRoom.GetEntity();
            if (homeRoom != null &&
                homeRoom.GetFloorIndex() > 0)
            {
                if (homeRoom.GetCenter().m_y < 40f)
                {
                    return commute >= -0.5f &&
                        commute <= -0.125f;
                }

                return commute >= -0.75f &&
                    commute <= -0.25f;
            }

            return commute >= -0.25f &&
                commute <= 0f;
        }

        private static string TagsLabel(
            string[] tags)
        {
            if (tags == null ||
                tags.Length == 0)
            {
                return "none";
            }

            string value =
                string.Empty;

            for (int i = 0;
                i < tags.Length;
                i++)
            {
                if (i > 0)
                {
                    value += ",";
                }

                value +=
                    string.IsNullOrEmpty(tags[i])
                        ? "none"
                        : tags[i];
            }

            return value;
        }

        private static string ObjectLabel(
            TileObject tileObject)
        {
            if (tileObject == null ||
                tileObject.m_state == null)
            {
                return "none";
            }

            string id =
                tileObject.GetType().Name;

            try
            {
                if (tileObject.m_state.m_gameDBObject != null &&
                    tileObject.m_state.m_gameDBObject.Entry != null &&
                    !ID.IsNullOrNoID(
                        tileObject.m_state.m_gameDBObject.Entry.DatabaseID))
                {
                    id =
                        tileObject.m_state.m_gameDBObject.Entry.DatabaseID.ToString();
                }
            }
            catch
            {
            }

            return id +
                "@" +
                VectorLabel(
                    tileObject.m_state.m_position,
                    tileObject.m_state.m_floorIndex);
        }

        private static string VectorLabel(
            Vector2i position,
            int floor)
        {
            return floor.ToString(
                    CultureInfo.InvariantCulture) +
                ":" +
                position.m_x.ToString(
                    CultureInfo.InvariantCulture) +
                "," +
                position.m_y.ToString(
                    CultureInfo.InvariantCulture);
        }

        internal static void DumpSummary()
        {
            if (!_enabled ||
                Plugin.Log == null ||
                _sync == null ||
                _counts == null)
            {
                return;
            }

            List<string> keys;
            lock (_sync)
            {
                keys =
                    new List<string>(_counts.Keys);
            }
            keys.Sort(StringComparer.Ordinal);

            Plugin.Log.LogInfo(
                "[Diagnostics] summary-begin");

            foreach (string key in keys)
            {
                int count;
                lock (_sync)
                {
                    if (!_counts.TryGetValue(
                            key,
                            out count))
                    {
                        continue;
                    }
                }

                Plugin.Log.LogInfo(
                    "[DiagnosticsSummary] " +
                    key +
                    " count=" +
                    count);
            }

            Plugin.Log.LogInfo(
                "[Diagnostics] summary-end");
        }

        internal static void Reset()
        {
            if (_sync != null)
            {
                lock (_sync)
                {
                    if (_counts != null)
                    {
                        _counts.Clear();
                    }
                    if (_rateStates != null)
                    {
                        _rateStates.Clear();
                    }
                }
            }

            _counts = null;
            _rateStates = null;
            _sync = null;
            _enabled = false;
        }

        private static void Record(
            string kind,
            string fix,
            string source,
            string outcome,
            string details,
            string repeatKey,
            int intervalMilliseconds)
        {
            if (!_enabled ||
                Plugin.Log == null ||
                _sync == null ||
                _counts == null)
            {
                return;
            }

            Increment(
                kind,
                fix,
                outcome);

            int suppressed = 0;
            if (!string.IsNullOrEmpty(repeatKey) &&
                intervalMilliseconds > 0)
            {
                if (_rateStates == null)
                {
                    return;
                }

                long now =
                    DateTime.UtcNow.Ticks;
                long interval =
                    (long)intervalMilliseconds *
                    TimeSpan.TicksPerMillisecond;

                lock (_sync)
                {
                    RateState state;
                    if (!_rateStates.TryGetValue(
                            repeatKey,
                            out state))
                    {
                        state =
                            new RateState();
                        _rateStates[repeatKey] = state;
                    }

                    if (state.LastTicks != 0 &&
                        now - state.LastTicks < interval)
                    {
                        state.Suppressed++;
                        return;
                    }

                    suppressed =
                        state.Suppressed;
                    state.Suppressed = 0;
                    state.LastTicks = now;
                }
            }

            Write(
                kind,
                fix,
                source,
                outcome,
                details,
                suppressed);
        }

        private static void Increment(
            string kind,
            string fix,
            string outcome)
        {
            string key =
                (kind ?? "unknown") +
                " | " +
                (fix ?? "unknown") +
                " | " +
                (outcome ?? "unknown");

            lock (_sync)
            {
                int count;
                _counts.TryGetValue(
                    key,
                    out count);
                _counts[key] =
                    count + 1;
            }
        }

        private static void Write(
            string kind,
            string fix,
            string source,
            string outcome,
            string details,
            int suppressed)
        {
            string message =
                "[Diagnostics] kind=" +
                Safe(kind) +
                " fix=" +
                Safe(fix) +
                " source=" +
                Safe(source) +
                " outcome=" +
                Safe(outcome);

            if (!string.IsNullOrEmpty(details))
            {
                message +=
                    " details=" +
                    Safe(details);
            }

            if (suppressed > 0)
            {
                message +=
                    " suppressed=" +
                    suppressed;
            }

            Plugin.Log.LogInfo(message);
        }

        private static string ClassifyOutcome(
            string outcome)
        {
            if (string.IsNullOrEmpty(outcome))
            {
                return "observed";
            }

            switch (outcome)
            {
                case "release-tracked":
                case "sequence-started":
                case "started":
                case "effect-active":
                case "normal-assignment":
                    return "observed";

                case "completed":
                case "restored":
                case "recovered":
                case "bed-restored":
                case "native-find-new-bed-started":
                case "bed-released-at-physical-pickup":
                case "waiting-room-recovered-after-hold":
                case "retry-arrival-confirmed":
                    return "result";

                case "recovery-failed":
                case "native-cleanup":
                case "native-rejected":
                case "retry-budget-exhausted":
                    return "fallback";

                default:
                    return "intervention";
            }
        }

        private static string Safe(
            string value)
        {
            return string.IsNullOrEmpty(value)
                ? "none"
                : value
                    .Replace('\r', ' ')
                    .Replace('\n', ' ');
        }
    }
}
