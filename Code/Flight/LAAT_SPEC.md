# LAAT/i gunship: build spec for the voxel replica

Sources checked on 2026-10-08:
- Wookieepedia, canon article "Low Altitude Assault Transport/infantry": specs, armament, crew and passengers.
- Wookieepedia, Legends article: bay handles, red and green cabin lights, articulated turret arms, drooping wings, sliding side doors, stern hatch.
- Its infobox still from *Attack of the Clones*.
- The Sketchfab render Angelo linked, used as a visual reference only. Nothing was downloaded or imported; our model stays original voxels.

Scale: 1 s&box unit = 1 inch (2.54 cm). A clone trooper is about 72 units (1.83 m).

## Size (built at true scale, nothing shrunk)
| | Canon | Ours (units) |
|---|---|---|
| Length | 17.69 m (17.4 m to gun clearance) | 696 (x -350 to +346), chin guns to about +385 |
| Width | 17 m wingspan | 670 (wingtips at y ±335) |
| Height | 6.1 m | 240 (gear on the ground to the top of the pilot's bubble) |
| Fuselage | — | about 4.2 m wide at the bay, about 2.4 m clear inside the bay |

## Shape
- **Fuselage:** a long, tall box with chamfered upper edges. The deck of the open troop bay sits amidships between the nose and the tail section.
- **Nose ("hunchbacked cockpit"):** the cockpit sits in single file and is modelled on the Mi-24 Hind.
  - The co-pilot/forward gunner sits in front and lower, in his own bubble.
  - The pilot sits behind and higher, in a second bubble on a raised hump.
  - Both bubbles are framed by the red hood.
  - Below the cockpit the nose drops to a rounded chin carrying the two chin turrets.
- **Wings:** two long, thin wings, mounted high on the roof behind the cockpit. They sweep back and droop (anhedral) toward red tips.
  - A remote bubble turret hangs under each wing, near the tip.
  - Four light air-to-air rockets hang under each wing (8 in all).
- **Dorsal launchers:** two mass-driver missile launchers (long cylinders) sit on the roof behind the cockpit, pointing forward. Missile belts run back from them along the spine.
- **Tail section:**
  - The fuselage tapers into the stern hatch, which is the aft rack for 4 speeder bikes.
  - The tail anti-personnel turret covers the hatch.
  - Two rear thrust engines are mounted high on the tail.
  - Repulsors run along the belly.
- **Landing gear:** four short legs, which fold up in flight in our version.

## Troop bay
- **Capacity:** a full platoon of 30 clone troopers, standing.
- **Grab handles:** they hang on cables from the ceiling, in two lines along the bay.
- **Cabin lights:** red while approaching, green when clear to debark. We use a red strip plus white ceiling light for now.
- **Doors:** one large sliding door on each side, running fore and aft along the hull. It slides **back** along rails over the tail section to open the whole bay side.
  - Animated open and closed.
  - Open by default in flight, for the films' look. The pilot toggles them.
- **Ball turrets:** the two manned ball turrets stand at the front corners of the bay, on articulated arms reaching out from the hull.

## Weapons (canon) and who fires them in SWRP
| Canon | Where | SWRP seat / key |
|---|---|---|
| 2 composite-beam pinpoint laser turrets, manned | Glass balls on arms at the bay's front corners | Left and right ball gunners, M1 |
| 2 composite-beam turrets, remote | Bubble turrets under the wings | Pilot, M1 |
| 2 dorsal mass-driver missile launchers, fed by rear belts | Roof, behind the cockpit | Pilot, M2 (6 missiles, reload over time) |
| 2 chin anti-personnel laser turrets | Under the nose | Co-pilot/forward gunner, M1 |
| 8 light air-to-air rockets | Under the wings | Co-pilot/forward gunner, M2 |
| 1 tail anti-personnel turret | Stern, over the hatch | Built and visible; **not yet manned** (canon has no extra crew for it) |

Laser bolts are green, as in *Attack of the Clones*.

## Crew and passengers in multiplayer
- **Crew:** pilot (rear bubble) and co-pilot/forward gunner (front bubble), both seated and visible through the glass. Two ball-turret gunners sit inside their balls.
- **Passengers:** 30 standing places in the bay: three rows of ten, facing the doors.
- **How they ride:** riders move with the ship on every screen and can get on and off at the doors (E). They're replicated through the ship's host-owned seat list. A second-client test is still to do.

## Livery
- Hull: Republic off-white with darker grey panels.
- Red: the cockpit hood, wing tips, the bands on the dorsal launchers, and the door trim.
- Battalion nose art: optional later.

## Not shown or not claimed
- **Tail fins:** not modelled. The reference views don't show the tail clearly enough to commit to fin shapes.
- **Inside the bay:** the speeder-bike rack, medical droid locker and blast shields are not modelled.
