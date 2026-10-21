# Project Hospital Mods — Roadmap

This roadmap shows public plans for the Project Hospital mods maintained here.
Plans may change as implementation, compatibility checks and in-game testing progress.

## Status

| Status | Items |
| --- | ---: |
| 🛠️ In development | 24 |
| 🟡 Planned | 44 |
| 💡 Ideas | 7 |
| ✅ Released | 15 |

## Hospital Always Lit (HAL)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Improvement | Localized Night Lighting | Use bedside lamps and other local light sources during nighttime care instead of lighting the entire room. |
| 🟡 Planned | Improvement | Stable Night Care Lighting | Prevent patient-room lights from flickering on and off while nighttime care is still in progress. |
| 💡 Ideas | Feature | Room Lighting Controllers | Control room lighting with simple presets such as Always On, Day, Night, or Night Economy. |

## Hospital Traffic Control (HTC)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| ✅ Released | Improvement | Safe Access Changes During Active Actions | Change access rules without breaking characters already using toilets, sinks, or other stationary objects. |

## Hospital Care Level Transfer (HCLT)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Feature | Automatic ICU-to-HDU Step-Down | Automatically move recovering ICU patients to HDU when they no longer require intensive care and a suitable bed is available. |
| 🟡 Planned | Improvement | Detailed Transfer Block Reasons | Show the specific reason a care-level transfer is temporarily unavailable, such as treatment, transport, stabilization, or active staff care. |
| 💡 Ideas | Compatibility | Respect “Do Not Send to HDU” Conditions | Correctly respect medical conditions that should not require HDU care when moving recovering patients back to regular wards. |

## Hospital Shift Handover (HSH)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Improvement | Janitor Shift Handover | Extend early shift handover so janitors can take over ongoing work more smoothly around shift changes. |
| 🟡 Planned | Compatibility | Porter Shift Handover Support | Integrate Hospital Porters with the early shift handover system. |

## Hospital EMS (HEMS)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Feature | EMS Arrival Pre-Alerts | Receive an advance notification and estimated arrival time before an ambulance or helicopter reaches the hospital. |
| 💡 Ideas | Feature | Tiered EMS Fleet — BLS, ALS & CCT | Use BLS, ALS, and Critical Care Transport units with different capabilities and dispatch roles. |
| 💡 Ideas | Feature | Weather-Based Air Ambulance Restrictions | Prevent helicopter operations when weather conditions make flying unsafe. |

## Hospital Patient Life (HPL)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Improvement | Missed Meal Recovery | Better handle hospitalized patients who miss a scheduled meal so their food needs can still be resolved appropriately. |
| 🟡 Planned | Feature | More Realistic Pharmacy Visits | Make medicine pickup feel more natural through improved interaction with pharmacists and pharmacy shelving. |
| 🟡 Planned | Feature | Patient Radios | Allow hospitalized patients to use radios as a personal leisure activity. |

## Hospital Radio (HRA)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🛠️ In development | Improvement | Audio Stability Fix | Prevent radio audio crashes and broken clips after using the F9 audio controls. |
| 🛠️ In development | Improvement | Prevent Double Track Skips | Prevent an old Next Track request from accidentally skipping more than one song. |
| 🟡 Planned | Feature | Hospital Emergency Announcements | Play contextual hospital announcements for major events such as ambulance arrivals and emergency codes. |
| 🟡 Planned | Feature | Spatial Radio Audio | Make music and announcements come from the radios actually placed around the hospital. |
| 🟡 Planned | Feature | Wall & Ceiling Speakers | Add dedicated hospital speakers for wider music and announcement coverage. |

## Hospital Porters (HPO)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Feature | Clinic Patient Escorting | Allow Porters to escort clinic patients to their destination when the normal patient flow supports it (new role). |
| 🟡 Planned | Feature | Multi-Lab Porter Rounds | Combine compatible laboratory deliveries into smarter multi-stop Porter routes instead of making unnecessary separate trips. |

## Hospital Flow Optimizer (HFO)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🛠️ In development | Improvement | Runtime Performance & Integration Optimization | Keep busy hospitals running more smoothly while improving compatibility with other mods. |
| 🟡 Planned | Improvement | Back-to-Back Surgery Scheduling | Reduce unnecessary operating room downtime by scheduling compatible planned surgeries more efficiently. |
| 🟡 Planned | Improvement | Diagnostic Queue Priorities | Prioritize diagnostic queues more intelligently while preserving medical urgency. |
| 🟡 Planned | Improvement | Direct Exam Chaining | Send patients directly from one planned examination to the next when possible instead of returning to their bed between every exam. |
| 🟡 Planned | Feature | Shared Radiology Technologist Pool | Let qualified technologists work across compatible X-ray, CT, and MRI rooms. |
| 🟡 Planned | Improvement | Smart Transport Wait — Wall-First Staging | Let transport staff wait in sensible nearby staging spots when appropriate. |
| 🟡 Planned | Feature | Trauma Holding & Boarding | Move stabilized patients out of valuable Trauma Center spaces while they wait for an appropriate inpatient bed. |
| ✅ Released | Improvement | Follow-Up Queue Priority | Give reasonable priority to patients returning with completed test results without blocking emergencies or new patients indefinitely. |
| ✅ Released | Compatibility | Hospital Porters Sample Continuation | Let Hospital Porters continue compatible sample transport work without unnecessary downtime. |
| ✅ Released | Improvement | Lab Queue Balancing | Spread new laboratory work between compatible labs to prevent one lab from becoming overloaded while another is underused. |
| ✅ Released | Improvement | Laboratory Waiting Room Reroute | Reroute patients to a suitable laboratory waiting room when needed. |
| ✅ Released | Improvement | Observation Capacity Notification | Notify you when observation capacity needs attention. |
| ✅ Released | Improvement | Protect Trauma Center Capacity | Avoid using Trauma Center rooms for generic examinations when other suitable rooms are available. |
| ✅ Released | Improvement | Smart Lab Task Assignment | Keep lab staff focused on useful pending work and completed results instead of inefficient task choices. |
| ✅ Released | Improvement | Smart Room Selection | Choose the best compatible examination room using distance, waiting time, current availability, and existing reservations. |
| ✅ Released | Improvement | Smart Transport Continuation | Reduce empty trips by letting transporters continue directly to another compatible nearby request. |
| ✅ Released | Improvement | Smarter Clinic Doctor Assignment | Balance clinic patients between suitable doctors using current workload, recent office activity, distance, and optional clinical matching. |
| ✅ Released | Improvement | Smarter Inpatient Doctor Assignment | Match hospitalized patients with suitable doctors based on workload and clinical needs. |
| ✅ Released | Improvement | Smarter Patient Transport Waiting | Let transport staff wait nearby during short examinations when returning and coming back again would create unnecessary travel. |
| ✅ Released | Improvement | Smarter Staff Need Destinations | Send staff to sensible nearby toilets and meal facilities instead of unnecessarily distant choices. |
| ✅ Released | Improvement | Transport Pressure Rescue | Improve transport behavior when the transport queue comes under heavy pressure. |

## Hospital Fixes (HFX)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🛠️ In development | Compatibility | Animate HSH Staff Clothing Changes | Keep Hospital Shift Handover staff clothing changes properly animated. |
| 🛠️ In development | Improvement | Animate Patient Clothing Changes | Show patient clothing changes with the proper in-game animation. |
| 🛠️ In development | Vanilla Fix | Avoid Redundant Exam Clothing Changes | Skip unnecessary clothing changes when a patient is already dressed appropriately. |
| 🛠️ In development | Vanilla Fix | Character Creator Age Limit Fix | Fix age-limit handling in the Character Creator. |
| 🛠️ In development | Vanilla Fix | Clarify Waiting Room Notifications | Make waiting-room notifications clearer about what needs attention. |
| 🛠️ In development | Vanilla Fix | Correct Employee Supervisor Display | Show the correct supervisor information on employee cards. |
| 🛠️ In development | Vanilla Fix | Elevator Wall Decorations | Allow signs, posters, decals, and other wall decorations to be placed on valid exterior walls around elevators. |
| 🛠️ In development | Vanilla Fix | Fair Recruitment Perk Reveal Costs | Prevent players from being charged unnecessarily when revealing candidate perks during recruitment. |
| 🛠️ In development | Vanilla Fix | Fix Character/Object Rendering Depth During Equipment Use | Keep staff visually layered correctly around equipment, animated objects, and lowered walls. |
| 🛠️ In development | Vanilla Fix | Fix Moving Object Lighting | Fix carts and other moving objects retaining the lighting from their previous position while being transported. |
| 🛠️ In development | Vanilla Fix | Fix Popup Text Overflow | Keep long popup text inside its window instead of spilling outside. |
| 🛠️ In development | Vanilla Fix | Fix Reserved Nurse Waiting Animation | Prevent reserved nurses from getting stuck in the wrong waiting animation. |
| 🛠️ In development | Vanilla Fix | Mobile Object Ghost Cleanup | Remove leftover visual or logical ghosts created by moved or deleted objects. |
| 🛠️ In development | Vanilla Fix | Pause After Loading Save | Keep the game paused after loading a save until you choose to resume. |
| 🛠️ In development | Vanilla Fix | Prevent Duplicate Janitor Cleaning | Stop multiple janitors from being assigned to clean the same target unnecessarily. |
| 🛠️ In development | Vanilla Fix | Prevent Immediate Observation Readmission | Stop patients from being sent straight back to observation immediately after leaving it. |
| 🛠️ In development | Vanilla Fix | Prevent Premature Outpatient Exit | Stop clinic patients from leaving before their required care is finished. |
| 🛠️ In development | Vanilla Fix | Remember Transparency Settings | Preserve the player's door, window, object and management-mode transparency preferences between game sessions. |
| 🛠️ In development | Vanilla Fix | Retry Collapse Care Staff Movement | Retry staff movement when collapse care is blocked by a temporary movement problem. |
| 🛠️ In development | Vanilla Fix | Safer Stretcher, Wheelchair and Cart Return | Keep stretchers, wheelchairs, and carts from disappearing when their usual return spot is blocked. |
| 🛠️ In development | Vanilla Fix | Validated Wall Object Placement | Reject invalid wall-object placement instead of allowing broken positions. |
| 🟡 Planned | Vanilla Fix | Correct Workspace Selector Titles | Display the correct profession in workspace selection windows. |
| 🟡 Planned | Vanilla Fix | Hide Former Employees from Staff Selectors | Prevent fired employees from continuing to appear in workspace and staff selection lists. |

## Hospital Clinical Procedures (HCP)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Feature | Dedicated Specimen Collection Rooms | Give clinic patients dedicated staffed rooms for blood, urine, and stool sample collection. |
| 🟡 Planned | Feature | Emergency Equipment in Hallways | Allow appropriate medical emergencies to use nearby emergency kits or defibrillators placed outside dedicated treatment rooms. |
| 🟡 Planned | Feature | Meal Cart Rounds | Allow nurses to use meal carts when serving food to hospitalized patients instead of handling meals one patient at a time. |
| 🟡 Planned | Feature | Medication Cart Rounds | Allow nurses to use medication carts when delivering medicines to hospitalized patients. |
| 🟡 Planned | Improvement | More Informative Symptom Tooltips | Show known mobility restrictions, contagiousness, and visible criticality in symptom tooltips without revealing hidden medical information. |
| 🟡 Planned | Improvement | Realistic Procedure Durations | Use more realistic and configurable durations for non-surgical examinations and procedures. |

## Hospital Patient Intake (HPI)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Feature | Clinic vs Emergency Routing | Route patients toward clinic or emergency care based on their intake and triage information. |
| 🟡 Planned | Feature | CTAS-Inspired Triage Levels | Give arriving patients an acuity level from 1 to 5 so the most urgent cases can be identified and prioritized more realistically. |
| 🟡 Planned | Feature | Department-Based Intake Routing | Allow intake rules to direct patients toward the most appropriate department before their normal care begins. |
| 🟡 Planned | Improvement | Just-in-Time Receptionist Assignment | Assign receptionists when intake actually needs them instead of reserving them too early. |
| 🟡 Planned | Feature | Shared Reception & Central Intake | Allow multiple departments to use shared reception and intake areas instead of requiring completely separate front desks. |

## Hospital Expansion (HEX)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Feature | Configurable Building Height | Allow hospitals to use more floors, with a configurable limit based on safe tested values. |
| 🟡 Planned | Feature | Larger Hospital Maps | Support hospital maps larger than the current 96×96 limit, starting with safely tested sizes such as 112×112 and potentially 128×128. |
| 🟡 Planned | Feature | Smart Vertical Travel | Choose between stairs and elevators based on character mobility, floor distance, accessibility, and realistic travel time. |
| 🟡 Planned | Feature | Usable Stairs | Add functional stairs as a true alternative to elevators for moving between hospital floors. |

## Hospital Staff Lifecycle (HSL)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Feature | Age-Based Retirement | Allow older employees to retire as their careers reach a natural end. |
| 🟡 Planned | Feature | Configurable Staff Aging | Let staff age over long-running hospitals with adjustable aging speed. |
| 🟡 Planned | Compatibility | Hospital Patient Life Leisure Support | Allow staff to use compatible leisure objects already enhanced by Hospital Patient Life, including supported wall decorations and bookshelves. |
| 🟡 Planned | Feature | Salary Progression | Support salary increases as part of long-term staff career progression. |
| 🟡 Planned | Feature | Staff Poaching & Retention Offers | Employees may receive outside offers, giving the player a chance to keep them with a retention bonus. |
| 🟡 Planned | Feature | Staff Resignations | Allow employees to leave the hospital as part of the staff lifecycle system. |

## Hospital Support Services (HSS)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 💡 Ideas | Feature | Central Kitchen | Add a centralized hospital kitchen that prepares and distributes meals for inpatient care. |
| 💡 Ideas | Feature | CSSD / Sterile Processing | Add a Central Sterile Services Department responsible for preparing and sterilizing reusable medical equipment. |
| 💡 Ideas | Feature | Hospital Laundry Service | Add a dedicated laundry service for handling hospital linens and related support work. |

---

Roadmap status reflects the current public development plan and is not a release guarantee.
