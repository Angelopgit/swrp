# Flight handling sheet (2026-10-21)

What each craft should feel like, as numbers the scripted course (`Swrp.Flight.SwrpFlightCourse.Run("arc170"|"laat")`,
log in data/anim_rec/course_<craft>.json) is checked against. Re-run the course after any change to the flight model.

## ARC-170: crisp, agile, light drift
| | target | measured (course) |
|---|---|---|
| top speed, full throttle | 3200 u/s (81 m/s), 1.45x on the afterburner | 3200 |
| 90° heading change from straight flight | under 2.5 s | 2.3 s |
| peak turn rate | 50-60°/s | 53°/s |
| bank into a hard turn | 60-70°, 90% of it within 1 s | 65°, 0.8 s |
| sideslip in turns ("light drift") | 4-8° | 5.9° (SlipGrip 0.6) |
| steering smoothness: turn-rate change step to step | no spikes; under 300°/s² | max 104°/s² (was 2,500 at slalom reversals) |
| braking, full reverse throttle | about a third of top speed in 3 s | 3200 -> 1083 u/s |
| dive 60° nose down, full throttle | gains speed (gravity along the nose) | 930 -> 3210 u/s in 1.6 s |

## LAAT/i: heavy, helicopter-like inertia, stable hover
| | target | measured |
|---|---|---|
| top speed | 1700 u/s (43 m/s) | 1700 |
| 90° heading change from a hover/cruise | 2-3 s, the turn winding up | 2.3 s |
| time to full turn speed | over a second | ~1.4 s (YawAccel 40) |
| peak turn rate | 50-60°/s | 55°/s |
| bank | gentle, under 25° | 24° |
| hold height with no input | drifts under 10 u/s | ~6 u/s up |
| sideslip | free: it turns its nose and keeps going the way it was (helicopter-like) | up to 80° in the hard turns |
| steering smoothness | under 100°/s² | max 60°/s² |

## Feel (local, the seat's own screen)
- Mouse aim eased by `swrp_flight_steer_smooth` (0.06 s); chase camera on a spring (`swrp_flight_camera_lag` 7) leaning with a third of the bank.
- Speed: FOV +7 at top speed, +9 on the afterburner; afterburner shake; desert wind loop by speed on a planet (space is silent); dust streaks past the camera (`swrp_flight_dust`).
- HUD: cockpit = pitch ladder (drawn on the world), flight path marker, heading tape, speed/alt boxes, afterburner bar, lock box and lead pip; chase = sight, FPM and a compact speed/alt/heat line. ALT only on a planet or near a deck; in space the Venator's range (VNTR, km). Squad, comlink, location and voice panels hide in any seat.

Not compared: Battlefront II timings (no footage or numbers from the game to measure against).
