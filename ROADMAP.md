# Project Hospital Mods — Roadmap

This roadmap shows public plans for the Project Hospital mods maintained here.
Plans may change as implementation, compatibility checks and in-game testing progress.

**Last updated:** 2026-11-07

## Status

| Status | Items |
| --- | ---: |
| 🛠️ In development | 14 |
| 🟡 Planned | 54 |
| 💡 Ideas | 44 |

## Hospital Always Lit (HAL)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Improvement | Localized Night Lighting | Use bedside lamps and other local light sources during nighttime care instead of lighting the entire room. |
| 🟡 Planned | Improvement | Stable Night Care Lighting | Prevent patient-room lights from flickering on and off while nighttime care is still in progress. |
| 💡 Ideas | Improvement | Localized Doctor Office Lighting | Use localized desk or workstation lighting at night without lighting the entire doctor's office. |
| 💡 Ideas | Feature | Room Lighting Controllers | Control room lighting with simple presets such as Always On, Day, Night, or Night Economy. |
| 💡 Ideas | Improvement | Vanilla Trauma Center Lighting | Let Trauma Center lighting follow Vanilla when HAL's nighttime automation could otherwise turn lights off between critical-care actions. |

## Hospital Traffic Control (HTC)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🛠️ In development | Improvement | 8-Direction Walking & Diagonal Route Smoothing | Make character movement feel less mechanical with safe diagonal corner smoothing and matching directional walking visuals. |
| 🟡 Planned | Improvement | Replan Personal Needs After Access Changes | Restart affected non-medical needs or leisure choices when an access change makes the current destination unusable. |
| 🟡 Planned | Vanilla Fix | Safer Access-Change Recovery Destinations | Prevent characters leaving newly restricted areas from being recovered onto occupied or invalid object tiles. |

## Hospital Care Level Transfer (HCLT)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Feature | Automatic ICU-to-HDU Step-Down | Automatically move recovering ICU patients to HDU when they no longer require intensive care and a suitable bed is available. |
| 🟡 Planned | Improvement | Care-Level Preemption While Waiting for a Bed | Allow a medically valid higher care-level request to replace a lower bed request while the patient is still waiting, avoiding unnecessary intermediate transfers. |
| 🟡 Planned | Improvement | Detailed Transfer Block Reasons | Show the specific reason a care-level transfer is temporarily unavailable, such as treatment, transport, stabilization, or active staff care. |
| 💡 Ideas | Compatibility | Respect “Do Not Send to HDU” Conditions | Correctly respect medical conditions that should not require HDU care when moving recovering patients back to regular wards. |

## Hospital Shift Handover (HSH)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Improvement | Clearer Shift-Handover Waiting Visual | Use a clearer, more immediately recognizable visual while staff are waiting for a shift handover. |
| 🟡 Planned | Improvement | Janitor Shift Handover | Extend early shift handover so janitors can take over ongoing work more smoothly around shift changes. |

## Hospital EMS (HEMS)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Feature | EMS Arrival Pre-Alerts | Receive an advance notification and estimated arrival time before an ambulance or helicopter reaches the hospital. |
| 💡 Ideas | Feature | CCT Inter-Hospital Transfers | Use Critical Care Transport units for abstracted transfers to another hospital when a patient needs external higher-level care. |
| 💡 Ideas | Feature | CCT Mutual Assistance | Allow a CCT unit to support another EMS unit on complex calls when multi-unit missions can be represented safely. |
| 💡 Ideas | Feature | Dedicated EMS Crews / SMUR | Assign real hospital staff to advanced EMS units for a more operational prehospital care system. |
| 💡 Ideas | Feature | EMS Call Acceptance & Refusal | Allow the player to accept or decline selected EMS calls when enough pre-dispatch information is available. |
| 💡 Ideas | Feature | EMS Treat & Release | Allow suitable EMS cases to be treated off-map and released without transport when the mission model can support it safely. |
| 💡 Ideas | Improvement | Smarter EMS Arrival Destinations | Route arriving EMS patients toward a more appropriate first hospital destination when the game already provides enough clinical information. |
| 💡 Ideas | Feature | Tiered EMS Fleet — BLS, ALS & CCT | Use BLS, ALS, and Critical Care Transport units with different capabilities and dispatch roles. |
| 💡 Ideas | Feature | Weather-Based Air Ambulance Restrictions | Prevent helicopter operations when weather conditions make flying unsafe. |

## Hospital Patient Life (HPL)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Improvement | Correct Placement Feedback for HPL-Usable Objects | Show appropriate placement feedback for objects that Hospital Patient Life can safely use even when Vanilla marks them as inaccessible. |
| 🟡 Planned | Improvement | Missed Meal Recovery | Better handle hospitalized patients who miss a scheduled meal so their food needs can still be resolved appropriately. |
| 🟡 Planned | Feature | More Realistic Pharmacy Visits | Make medicine pickup feel more natural through improved interaction with pharmacists and pharmacy shelving. |
| 🟡 Planned | Feature | Patient Radios | Allow hospitalized patients to use radios as a personal leisure activity. |
| 💡 Ideas | Improvement | More Interactive Obstructed Decorations | Extend HPL support to additional yellow/orange decorations whose real interaction point remains usable despite a harmless visual obstruction. |
| 💡 Ideas | Improvement | More Patient Leisure Seating | Let suitable non-waiting seats and benches support patient leisure when their native interaction and reservation rules make that safe. |
| 💡 Ideas | Feature | Patient Amenities & Coffee Shops | Let patients use suitable native amenities when those objects have real interactions, reservations and need effects. |
| 💡 Ideas | Feature | Per-Cafeteria Access Policies | Allow cafeterias to use policies such as Staff Only, Patients Only, Visitors Only, or Everyone. |
| 💡 Ideas | Improvement | Smarter Patient Visitors | Improve visitor behavior by reusing the native visitor system for more natural patient visits. |

## Hospital Radio (HRA)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Feature | Hospital Emergency Announcements | Play contextual hospital announcements for major events such as ambulance arrivals and emergency codes. |
| 🟡 Planned | Feature | Spatial Radio Audio | Make music and announcements come from the radios actually placed around the hospital. |
| 🟡 Planned | Feature | Wall & Ceiling Speakers | Add dedicated hospital speakers for wider music and announcement coverage. |

## Hospital Porters (HPO)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Feature | Physical Multi-Lab Sample Deliveries | Let Porters physically deliver different parts of a sample bundle to their actual laboratory destinations during one multi-stop route. |
| 🟡 Planned | Feature | Walking Escort for Autonomous Clinic Patients | Allow Porters to accompany suitable mobile clinic patients on foot when Vanilla would otherwise have them walk to a distant destination alone. |
| 💡 Ideas | Improvement | Urgent Patient Transport Priority During Sample Runs | Allow urgent patient transport work to take priority over compatible sample duties without corrupting an active Porter mission. |

## Hospital Flow Optimizer (HFO)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Improvement | Back-to-Back Surgery Scheduling | Reduce unnecessary operating room downtime by scheduling compatible planned surgeries more efficiently. |
| 🟡 Planned | Improvement | Cross-Lab Sample & Workflow Coherence | Keep laboratory task selection consistent with the sample's real laboratory and physical location while preserving explicit transport when needed. |
| 🟡 Planned | Improvement | Diagnostic Queue Priorities | Prioritize diagnostic queues more intelligently while preserving medical urgency. |
| 🟡 Planned | Improvement | Direct Exam Chaining | Send patients directly from one planned examination to the next when possible instead of returning to their bed between every exam. |
| 🟡 Planned | Improvement | Direct Post-Exam Destination | Skip an unnecessary return to bed after an examination when the next native destination can be reached directly without breaking reservations or patient flow. |
| 🟡 Planned | Improvement | Direct Transfer to Scheduled Surgery | Avoid an unnecessary intermediate ward-bed transfer when an already hospitalized patient can safely proceed to a scheduled surgery. |
| 🟡 Planned | Feature | Shared Radiology Technologist Pool | Let qualified technologists work across compatible X-ray, CT, and MRI rooms. |
| 🟡 Planned | Improvement | Trauma Center to Observation Step-Down | Move stabilized patients from Trauma Center to compatible Observation capacity when that level of care is already clinically valid. |
| 🟡 Planned | Feature | Trauma Holding & Boarding | Move stabilized patients out of valuable Trauma Center spaces while they wait for an appropriate inpatient bed. |
| 💡 Ideas | Improvement | Keep Compatible Trauma Exams in the Trauma Center | Keep Trauma patients in their current Trauma Center for compatible simple examinations when the required equipment is actually available there. |
| 💡 Ideas | Improvement | Laboratory Equipment Bottleneck Alerts | Warn when a laboratory is persistently blocked by a specific required machine rather than by ordinary queue delay. |
| 💡 Ideas | Improvement | Remove Individual Janitor Room Assignments | Allow a single assigned room to be removed from a janitor without clearing every room assignment. |
| 💡 Ideas | Improvement | Reserved Procedure Room & Equipment Coherence | Keep room-local equipment in the same room as the already reserved procedure scene to avoid unnecessary Trauma/Observation ping-pong. |
| 💡 Ideas | Improvement | Smart Janitor Task Assignment | Order already-admissible cleaning work more intelligently using factors such as distance, urgency, dirt and existing assignments. |
| 💡 Ideas | Improvement | Smart Staff Leisure Selection | Prefer sensible nearby leisure objects for staff free time while preserving native perks, reservations and eligibility. |

## Hospital Fixes (HFX)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Vanilla Fix | Correct Stretcher Footprint Around Transport Points | Prevent moving stretchers from overlapping nearby blocking objects when their full physical footprint does not fit. |
| 🟡 Planned | Vanilla Fix | Correct Workspace Selector Titles | Display the correct profession in workspace selection windows. |
| 🟡 Planned | Vanilla Fix | Fix Remote Patient Calls After Department Transfers | Prevent patients from being called remotely from an old waiting area after their department or procedure destination has changed. |
| 🟡 Planned | Vanilla Fix | Hide Former Employees from Staff Selectors | Prevent fired employees from continuing to appear in workspace and staff selection lists. |
| 🟡 Planned | Vanilla Fix | Prevent Duplicate & Phantom Bed Ownership | Prevent patients from retaining multiple beds and leaving unusable ghost bed ownership after transfer or departure. |
| 🟡 Planned | Vanilla Fix | Prevent Observation Discharge With Active Inpatient Lab Work | Keep Observation discharge from breaking a still-active inpatient laboratory workflow. |
| 🟡 Planned | Compatibility | Restore HSH Clothing Animation Compatibility | Restore the optional HSH clothing-change visual integration after HSH's handover method signature changed. |
| 🟡 Planned | Vanilla Fix | Restore Workspace Selector Filters After Late UI Initialization | Keep workspace employee selectors in their intended simplified filter state even when the underlying employee table initializes later. |
| 💡 Ideas | Vanilla Fix | Clean Up Truly Orphaned Lab Samples | Remove laboratory samples only when their workflow is provably impossible to continue, without blocking discharge or deleting still-valid results. |
| 💡 Ideas | Vanilla Fix | Correct Clothing-Change Z-Depth While Sitting or Lying | Keep clothing-change visual effects correctly occluded by beds, tables and equipment. |
| 💡 Ideas | Vanilla Fix | Doctor Mode Multi-Treatment Discharge Fix | Prevent controlled patients from becoming stuck after only part of a required multi-treatment discharge sequence is completed. |
| 💡 Ideas | Improvement | Earlier Outpatient Redressing | Return outpatients to normal clothing at the clinically correct point when leaving an exam-room sequence. |
| 💡 Ideas | Improvement | Lying Patient Waiting Animations | Use a suitable native lying idle animation when a real clinical waiting state would otherwise leave a patient visually frozen. |
| 💡 Ideas | Vanilla Fix | Prioritize Critical Symptom Treatment Before Long Exams | Prevent a known critical symptom from being left untreated while the patient is sent to a long diagnostic procedure when Vanilla already has the required treatment available. |
| 💡 Ideas | Vanilla Fix | Recover Stuck Ambulances After Interrupted Admission | Recover ambulance state safely when an interrupted admission leaves the vehicle unable to return to service. |
| 💡 Ideas | Improvement | Surgery Clothing-Change Visuals | Show clothing-change effects when a surgical workflow actually changes the patient's clothing. |
| 💡 Ideas | Improvement | Switch Departments Directly From Logistics View | Allow clicking another department in Logistics view to switch the displayed department without leaving that view. |
| 💡 Ideas | Vanilla Fix | Usable Wall Object Depth Conflicts | Fix visual depth conflicts between usable wall-mounted objects and nearby furniture without reintroducing broad placement restrictions. |

## Hospital Clinical Procedures (HCP)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🛠️ In development | Feature | Bedside Clinical Procedures | Perform suitable simple non-surgical procedures at a hospitalized patient's bedside when the required staff and equipment are available. |
| 🛠️ In development | Feature | Dedicated Specimen Collection Rooms | Give clinic patients dedicated staffed rooms for blood, urine, and stool sample collection. |
| 🛠️ In development | Feature | Emergency Equipment in Hallways | Allow appropriate medical emergencies to use nearby emergency kits or defibrillators placed outside dedicated treatment rooms. |
| 🛠️ In development | Improvement | Grouped Specimen Collection | Avoid repeating compatible specimen collection steps when one real collection can support several already-planned laboratory tests. |
| 🛠️ In development | Feature | Meal Cart Rounds | Allow nurses to use meal carts when serving food to hospitalized patients instead of handling meals one patient at a time. |
| 🛠️ In development | Feature | Medical Room & Equipment Assignment | Allow selected medical rooms or equipment to be assigned to specific departments or care contexts. |
| 🛠️ In development | Feature | Medication Cart Rounds | Allow nurses to use medication carts when delivering medicines to hospitalized patients. |
| 🛠️ In development | Improvement | More Informative Symptom Tooltips | Show known mobility restrictions, contagiousness, and visible criticality in symptom tooltips without revealing hidden medical information. |
| 🛠️ In development | Feature | Outpatient Laboratory Collection Workflow | Route mobile outpatients through laboratory reception, collection waiting, specimen collection and sample handoff before native laboratory analysis and results. |
| 🛠️ In development | Feature | Outpatient Rooming & Exam Rooms | Room clinic patients in dedicated examination rooms before the appropriate clinician joins them. |
| 🛠️ In development | Feature | Patient Changing Booths & Privacy Screens | Add a more coherent place for patients to change clothing around procedures that require it. |
| 🛠️ In development | Improvement | Patient Hygiene After Urine Sampling | Have patients wash and dry their hands after urine sampling using suitable nearby hygiene facilities before continuing their care. |
| 🛠️ In development | Improvement | Realistic Procedure Durations | Use more realistic and configurable durations for non-surgical examinations and procedures. |

## Hospital Patient Intake (HPI)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Feature | Clinic vs Emergency Routing | Route patients toward clinic or emergency care based on their intake and triage information. |
| 🟡 Planned | Feature | CTAS-Inspired Triage Levels | Give arriving patients an acuity level from 1 to 5 so the most urgent cases can be identified and prioritized more realistically. |
| 🟡 Planned | Feature | Department-Based Intake Routing | Allow intake rules to direct patients toward the most appropriate department before their normal care begins. |
| 🟡 Planned | Improvement | Early Department Routing When Diagnoses Converge | Move a patient toward the appropriate department once every remaining possible diagnosis belongs to that department. |
| 🟡 Planned | Improvement | Just-in-Time Receptionist Assignment | Assign receptionists when intake actually needs them instead of reserving them too early. |
| 🟡 Planned | Improvement | Mandatory Walk-In Intake Before Waiting Room | Have eligible walk-in patients pass through a real reception or triage step instead of silently bypassing intake. |
| 🟡 Planned | Feature | Shared Reception & Central Intake | Allow multiple departments to use shared reception and intake areas instead of requiring completely separate front desks. |
| 💡 Ideas | Feature | Trauma Center & Observation Intake Routing | Route appropriately triaged patients into the native Trauma Center or Observation workflow when the game's clinical state and hospitalization rules make that safe. |
| 💡 Ideas | Improvement | Triage Reassessment While Waiting | Reassess waiting patients so meaningful deterioration can update their intake priority. |

## Hospital Expansion (HEX)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Feature | Configurable Building Height | Allow hospitals to use more floors, with a configurable limit based on safe tested values. |
| 🟡 Planned | Feature | Larger Hospital Maps | Support hospital maps larger than the current 96×96 limit, starting with safely tested sizes such as 112×112 and potentially 128×128. |
| 🟡 Planned | Feature | Smart Vertical Travel | Choose between stairs and elevators based on character mobility, floor distance, accessibility, and realistic travel time. |
| 🟡 Planned | Feature | Usable Stairs | Add functional stairs as a true alternative to elevators for moving between hospital floors. |
| 💡 Ideas | Feature | Diagonal & Curved Walls | Extend hospital architecture with 45-degree diagonal walls first and modular rounded corners later. |

## Hospital Staff Lifecycle (HSL)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 🟡 Planned | Feature | Age-Based Retirement | Allow older employees to retire as their careers reach a natural end. |
| 🟡 Planned | Feature | Configurable Staff Aging | Let staff age over long-running hospitals with adjustable aging speed. |
| 🟡 Planned | Compatibility | Hospital Patient Life Leisure Support | Allow staff to use compatible leisure objects already enhanced by Hospital Patient Life, including supported wall decorations and bookshelves. |
| 🟡 Planned | Feature | Salary Progression | Support salary increases as part of long-term staff career progression. |
| 🟡 Planned | Feature | Staff Poaching & Retention Offers | Employees may receive outside offers, giving the player a chance to keep them with a retention bonus. |
| 🟡 Planned | Feature | Staff Resignations | Allow employees to leave the hospital as part of the staff lifecycle system. |
| 🟡 Planned | Improvement | Visual Staff Aging | Update suitable staff appearance details as employees move through age categories during long-running hospitals. |
| 💡 Ideas | Feature | Positive Career Events | Add occasional positive career events that make long-term staff development feel more alive. |
| 💡 Ideas | Feature | Professional Conduct & Discipline Events | Add bounded career events related to professional conduct and disciplinary outcomes. |

## Hospital Support Services (HSS)

| Status | Type | Feature | Description |
| --- | --- | --- | --- |
| 💡 Ideas | Feature | Administrative Leadership | Add physical administrative leadership roles that provide concrete hospital-management functions within the existing administrative department. |
| 💡 Ideas | Feature | Central Kitchen | Add a centralized hospital kitchen that prepares and distributes meals for inpatient care. |
| 💡 Ideas | Feature | Central Supply & Consumable Restocking | Add a central supply service for hospital consumables with bounded stock and replenishment gameplay. |
| 💡 Ideas | Feature | CSSD / Sterile Processing | Add a Central Sterile Services Department responsible for preparing and sterilizing reusable medical equipment. |
| 💡 Ideas | Feature | Hospital Laundry Service | Add a dedicated laundry service for handling hospital linens and related support work. |

---

Roadmap status reflects the current public development plan and is not a release guarantee.
