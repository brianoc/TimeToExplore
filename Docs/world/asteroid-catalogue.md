---
type: Catalogue
title: Asteroid catalogue
description: The 100 largest main-belt asteroids in the starting world, with diameters and orbital elements.
tags: [world, catalogue, asteroids, belt]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-15T00:00:00Z }
sources:
  - id: jpl-sbdb
    resource: https://ssd-api.jpl.nasa.gov/sbdb_query.api
    title: JPL Small-Body Database Query API
    author: team:jpl-ssd
---

# Selection

The belt catalogue is the 100 largest main-belt asteroids by effective diameter
in the JPL Small-Body Database, filtered to `class = MBA` and semi-major axis
between 2.0 and 3.5 AU.[^jpl-sbdb] It runs from Ceres (939 km) to Elfriede
(131 km).

Objects are keyed `ast_<number>` in the data file so that asteroid 52 Europa and
the moon Europa do not collide. The display name is the IAU name, except for
the three asteroids whose names collide with a major moon — 52 Europa, 85 Io,
and 106 Dione — which carry their catalogue number in the display name too.

# Table

| # | Name | Diameter (km) | a (AU) | e | i (°) |
|---:|---|---:|---:|---:|---:|
| 1 | Ceres | 939.4 | 2.766 | 0.0797 | 10.59 |
| 2 | Vesta | 522.8 | 2.361 | 0.0902 | 7.14 |
| 3 | Pallas | 513.0 | 2.770 | 0.2307 | 34.93 |
| 4 | Hygiea | 407.1 | 3.151 | 0.1067 | 3.83 |
| 5 | Interamnia | 306.3 | 3.057 | 0.1551 | 17.32 |
| 6 | Europa | 303.9 | 3.094 | 0.1125 | 7.48 |
| 7 | Davida | 270.3 | 3.162 | 0.1894 | 15.95 |
| 8 | Euphrosyne | 267.1 | 3.162 | 0.2158 | 26.31 |
| 9 | Patientia | 253.9 | 3.065 | 0.0695 | 15.21 |
| 10 | Juno | 246.6 | 2.671 | 0.2557 | 12.99 |
| 11 | Thisbe | 232.0 | 2.766 | 0.1656 | 5.23 |
| 12 | Eunomia | 231.7 | 2.642 | 0.1878 | 11.76 |
| 13 | Psyche | 222.0 | 2.926 | 0.1349 | 3.10 |
| 14 | Bamberga | 220.7 | 2.683 | 0.3404 | 11.10 |
| 15 | Doris | 216.5 | 3.114 | 0.0661 | 6.56 |
| 16 | Ursula | 216.0 | 3.127 | 0.1022 | 15.94 |
| 17 | Lomia | 209.0 | 2.992 | 0.0228 | 14.92 |
| 18 | Dione | 207.9 | 3.182 | 0.1587 | 4.57 |
| 19 | Daphne | 205.5 | 2.767 | 0.2702 | 15.73 |
| 20 | Aurora | 204.9 | 3.159 | 0.0973 | 7.97 |
| 21 | Egeria | 202.6 | 2.576 | 0.0846 | 16.53 |
| 22 | Eugenia | 202.3 | 2.723 | 0.0814 | 6.61 |
| 23 | Fortuna | 200.0 | 2.442 | 0.1581 | 1.57 |
| 24 | Iris | 199.8 | 2.386 | 0.2303 | 5.52 |
| 25 | Themis | 198.0 | 3.144 | 0.1154 | 0.74 |
| 26 | Bertha | 192.6 | 3.194 | 0.0772 | 21.02 |
| 27 | Alauda | 191.0 | 3.192 | 0.0153 | 20.60 |
| 28 | Metis | 190.0 | 2.387 | 0.1225 | 5.58 |
| 29 | Amphitrite | 189.6 | 2.555 | 0.0732 | 6.08 |
| 30 | Hebe | 185.2 | 2.426 | 0.2023 | 14.74 |
| 31 | Elektra | 180.7 | 3.125 | 0.2097 | 22.78 |
| 32 | Loreley | 180.1 | 3.126 | 0.0835 | 11.23 |
| 33 | Laetitia | 179.5 | 2.769 | 0.1138 | 10.38 |
| 34 | Dido | 179.0 | 3.154 | 0.0520 | 7.17 |
| 35 | Aegle | 177.8 | 3.048 | 0.1425 | 15.98 |
| 36 | Diotima | 175.9 | 3.068 | 0.0333 | 11.24 |
| 37 | Aletheia | 174.3 | 3.133 | 0.1300 | 10.81 |
| 38 | Palma | 173.6 | 3.163 | 0.2523 | 23.78 |
| 39 | Winchester | 171.7 | 2.999 | 0.3398 | 18.22 |
| 40 | Aspasia | 171.0 | 2.576 | 0.0735 | 11.28 |
| 41 | Germania | 168.9 | 3.046 | 0.1062 | 5.52 |
| 42 | Aglaja | 168.2 | 2.879 | 0.1340 | 4.97 |
| 43 | Kreusa | 168.1 | 3.168 | 0.1585 | 11.48 |
| 44 | Herculina | 167.8 | 2.770 | 0.1804 | 16.30 |
| 45 | Kalliope | 167.5 | 2.909 | 0.0988 | 13.70 |
| 46 | Pales | 166.3 | 3.097 | 0.2213 | 3.20 |
| 47 | Elpis | 165.1 | 2.713 | 0.1165 | 8.65 |
| 48 | Siegena | 165.0 | 2.899 | 0.1696 | 20.22 |
| 49 | Nemesis | 162.5 | 2.750 | 0.1269 | 6.25 |
| 50 | Prokne | 161.7 | 2.619 | 0.2375 | 18.53 |
| 51 | Zelinda | 160.7 | 2.297 | 0.2317 | 18.10 |
| 52 | Eunike | 160.5 | 2.738 | 0.1268 | 23.25 |
| 53 | Lucina | 160.3 | 2.721 | 0.0639 | 13.09 |
| 54 | Alexandra | 160.1 | 2.710 | 0.1992 | 11.84 |
| 55 | Scheila | 159.7 | 2.933 | 0.1595 | 14.62 |
| 56 | Gyptis | 159.3 | 2.771 | 0.1733 | 10.27 |
| 57 | Hypatia | 155.7 | 2.905 | 0.0917 | 12.41 |
| 58 | Lachesis | 155.1 | 3.120 | 0.0490 | 6.94 |
| 59 | Io | 154.8 | 2.654 | 0.1930 | 11.96 |
| 60 | Minerva | 154.2 | 2.753 | 0.1430 | 8.54 |
| 61 | Irene | 152.0 | 2.588 | 0.1628 | 9.13 |
| 62 | Berbericia | 151.7 | 2.931 | 0.1650 | 18.25 |
| 63 | Juewa | 151.1 | 2.786 | 0.1703 | 10.87 |
| 64 | Eleonora | 149.0 | 2.801 | 0.1119 | 18.36 |
| 65 | Aurelia | 148.7 | 2.595 | 0.2523 | 3.93 |
| 66 | Papagena | 148.1 | 2.890 | 0.2295 | 15.02 |
| 67 | Arethusa | 148.0 | 3.065 | 0.1524 | 13.01 |
| 68 | Flora | 147.5 | 2.202 | 0.1562 | 5.89 |
| 69 | Pulcova | 147.3 | 3.152 | 0.1080 | 13.10 |
| 70 | Lamberta | 147.3 | 2.732 | 0.2383 | 10.57 |
| 71 | Gudrun | 145.7 | 3.109 | 0.1078 | 16.13 |
| 72 | Liguria | 145.5 | 2.754 | 0.2420 | 8.21 |
| 73 | Julia | 145.5 | 2.551 | 0.1837 | 16.12 |
| 74 | Philomela | 144.6 | 3.118 | 0.0126 | 7.25 |
| 75 | Adorea | 144.6 | 3.091 | 0.1373 | 2.44 |
| 76 | Xanthippe | 143.3 | 2.733 | 0.2215 | 9.76 |
| 77 | Parthenope | 142.9 | 2.452 | 0.1009 | 4.64 |
| 78 | Vibilia | 142.4 | 2.654 | 0.2354 | 4.82 |
| 79 | Isolda | 141.1 | 3.048 | 0.1544 | 3.87 |
| 80 | Dembowska | 139.8 | 2.925 | 0.0885 | 8.25 |
| 81 | Melpomene | 139.6 | 2.296 | 0.2178 | 10.13 |
| 82 | Comacina | 139.4 | 3.151 | 0.0491 | 13.00 |
| 83 | Hedwig | 138.5 | 2.650 | 0.0750 | 10.93 |
| 84 | Nemausa | 138.2 | 2.366 | 0.0660 | 9.97 |
| 85 | Hesperia | 138.1 | 2.977 | 0.1689 | 8.59 |
| 86 | Hispania | 138.0 | 2.838 | 0.1416 | 15.39 |
| 87 | Hippo | 137.6 | 2.887 | 0.1067 | 19.50 |
| 88 | Klymene | 136.6 | 3.145 | 0.1630 | 2.79 |
| 89 | Medea | 136.1 | 3.117 | 0.1028 | 4.28 |
| 90 | Massalia | 135.7 | 2.408 | 0.1436 | 0.71 |
| 91 | Wratislavia | 134.7 | 3.152 | 0.1758 | 11.24 |
| 92 | Argentina | 133.7 | 3.179 | 0.1617 | 11.45 |
| 93 | Circe | 133.0 | 2.688 | 0.1065 | 5.50 |
| 94 | Protogeneia | 132.9 | 3.137 | 0.0218 | 1.93 |
| 95 | Atalante | 132.8 | 2.745 | 0.3062 | 18.37 |
| 96 | Ianthe | 132.8 | 2.688 | 0.1878 | 15.58 |
| 97 | Emma | 132.4 | 3.049 | 0.1440 | 7.99 |
| 98 | Erminia | 132.3 | 2.922 | 0.0521 | 25.02 |
| 99 | Hestia | 131.5 | 2.526 | 0.1718 | 2.35 |
| 100 | Elfriede | 131.3 | 3.190 | 0.0682 | 17.06 |

# Playable scale

At the default render scale, Ceres has a radius of about 0.47 units (470 km in
render space) and the smallest catalogue asteroid about 0.066 units. Both are
sub-pixel at typical viewing distances. The renderer therefore applies a
minimum apparent size of a few pixels, so asteroids read as markers rather than
disappearing. Press `T` in the prototype to see the true scale, where most of
the belt becomes invisible—as it would in reality.

# Gravity

Asteroid `GM` values are taken from JPL where available, and estimated from a
bulk density of 2.5 g/cm³ otherwise. The largest objects have measurable gravity:
Ceres is about 1.5% of Earth's surface gravity, and Vesta about 1.3%. For most of
the catalogue, gravity is negligible compared with the ship's 10 m/s² thrust.
Docking to an asteroid is therefore more about matching velocity than about
orbiting it.

# Known limits

* Diameters are effective diameters, not triaxial shapes.
* Elements are osculating at the database epoch and advanced to the game epoch
  with a two-body propagation.
* Close approaches and perturbations are not modelled.
* Rotation periods are real where JPL supplied them, otherwise a deterministic
  placeholder is used.

[^jpl-sbdb]: JPL, *Small-Body Database Query API*.
