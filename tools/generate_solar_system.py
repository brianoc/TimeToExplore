#!/usr/bin/env python3
"""
Generate the starting solar system for Time to Explore.

Sources
-------
* Planets: JPL SSD "Approximate Positions of the Planets" (Standish & Williams),
  mean ecliptic and equinox of J2000, Table 1 (valid 1800-2050).
* Asteroids: NASA/JPL Small-Body Database Query API, osculating elements and
  effective diameters. We keep the 100 largest main-belt asteroids.
* Moons: IAU/JPL physical constants. Moon orbit phases at epoch are authored
  (we do not have a full satellite ephemeris in this prototype); planes,
  semi-major axes, eccentricities and periods are real.

The output is a single JSON file consumed by Unity's JsonUtility. All lengths
are metres, all times seconds, all angles degrees in the file (converted to
radians at load). The simulation epoch is the game start date.

Usage
-----
    python3 tools/generate_solar_system.py [--offline] [--output PATH]

`--offline` reuses tools/cache/asteroids.json if present. `--output` writes
somewhere other than the committed catalogue, which the check script uses to
prove the generator still reproduces the file byte for byte.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import urllib.parse
import urllib.request
from datetime import datetime, timezone

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CACHE = os.path.join(ROOT, "tools", "cache", "asteroids.json")
OUT = os.path.join(
    ROOT, "TimeToExplore", "Assets", "Resources", "solar_system.json"
)

AU_M = 149_597_870_700.0
DAY_S = 86_400.0
J2000 = 2_451_545.0

# 2026-11-15 00:00:00 UTC. Game start / common element epoch.
START_JD = 2_461_359.5

# ---------------------------------------------------------------------------
# Planets: JPL Table 1 elements and rates (a AU, e, I deg, L deg, peri deg,
# node deg; rates per Julian century), plus physical data.
# ---------------------------------------------------------------------------
PLANETS = [
    {
        "id": "mercury", "name": "Mercury",
        "elements": (0.38709927, 0.00000037, 0.20563593, 0.00001906,
                     7.00497902, -0.00594749, 252.25032350, 149472.67411175,
                     77.45779628, 0.16047689, 48.33076593, -0.12534081),
        "radius_km": 2439.7, "gm": 2.2032e13,
        "rotation_h": 1407.6, "tilt": 0.034,
        "pole_ra": 281.01, "pole_dec": 61.42,
        "color": [0.56, 0.52, 0.46],
        "description": "Smallest planet; large iron core; 3:2 spin-orbit resonance.",
    },
    {
        "id": "venus", "name": "Venus",
        "elements": (0.72333566, 0.00000390, 0.00677672, -0.00004107,
                     3.39467605, -0.00078890, 181.97909950, 58517.81538729,
                     131.60246718, 0.00268329, 76.67984255, -0.27769418),
        "radius_km": 6051.8, "gm": 3.24859e14,
        "rotation_h": -5832.5, "tilt": 177.36,
        "pole_ra": 272.76, "pole_dec": 67.16,
        "color": [0.86, 0.79, 0.64],
        "description": "Runaway-greenhouse world with a retrograde rotation.",
    },
    {
        "id": "earth", "name": "Earth",
        "elements": (1.00000261, 0.00000562, 0.01671123, -0.00004392,
                     -0.00001531, -0.01294668, 100.46457166, 35999.37244981,
                     102.93768193, 0.32327364, 0.0, 0.0),
        "radius_km": 6371.0, "gm": 3.986004418e14,
        "rotation_h": 23.9345, "tilt": 23.44,
        "pole_ra": 0.0, "pole_dec": 90.0,
        "color": [0.20, 0.42, 0.72],
        "description": "Home world; the Earth-Moon barycentre is used for the heliocentric orbit.",
    },
    {
        "id": "mars", "name": "Mars",
        "elements": (1.52371034, 0.00001847, 0.09339410, 0.00007882,
                     1.84969142, -0.00813131, -4.55343205, 19140.30268499,
                     -23.94362959, 0.44441088, 49.55953891, -0.29257343),
        "radius_km": 3389.5, "gm": 4.282837e13,
        "rotation_h": 24.6229, "tilt": 25.19,
        "pole_ra": 317.68, "pole_dec": 52.89,
        "color": [0.72, 0.38, 0.24],
        "description": "Cold desert world with two small captured moons.",
    },
    {
        "id": "jupiter", "name": "Jupiter",
        "elements": (5.20288700, -0.00011607, 0.04838624, -0.00013253,
                     1.30439695, -0.00183714, 34.39644051, 3034.74612775,
                     14.72847983, 0.21252668, 100.47390909, 0.20469106),
        "radius_km": 69911.0, "gm": 1.26686534e17,
        "rotation_h": 9.925, "tilt": 3.13,
        "pole_ra": 268.06, "pole_dec": 64.50,
        "color": [0.80, 0.70, 0.56],
        "description": "Largest planet; four Galilean moons; strong radiation belts.",
    },
    {
        "id": "saturn", "name": "Saturn",
        "elements": (9.53667594, -0.00125060, 0.05386179, -0.00050991,
                     2.48599187, 0.00193609, 49.95424423, 1222.49362201,
                     92.59887831, -0.41897216, 113.66242448, -0.28867794),
        "radius_km": 58232.0, "gm": 3.7931187e16,
        "rotation_h": 10.656, "tilt": 26.73,
        "pole_ra": 40.59, "pole_dec": 83.54,
        "color": [0.86, 0.79, 0.62],
        "description": "Ringed gas giant; Titan is the only moon with a dense atmosphere.",
    },
    {
        "id": "uranus", "name": "Uranus",
        "elements": (19.18916464, -0.00196176, 0.04725744, -0.00004397,
                     0.77263783, -0.00242939, 313.23810451, 428.48202785,
                     170.95427630, 0.40805281, 74.01692503, 0.04240589),
        "radius_km": 25362.0, "gm": 5.793939e15,
        "rotation_h": -17.24, "tilt": 97.77,
        "pole_ra": 257.31, "pole_dec": -15.18,
        "color": [0.56, 0.79, 0.83],
        "description": "Ice giant tipped on its side; its moon system shares the extreme tilt.",
    },
    {
        "id": "neptune", "name": "Neptune",
        "elements": (30.06992276, 0.00026291, 0.00859048, 0.00005105,
                     1.77004347, 0.00035372, -55.12002969, 218.45945325,
                     44.96476227, -0.32241464, 131.78422574, -0.00508664),
        "radius_km": 24622.0, "gm": 6.836529e15,
        "rotation_h": 16.11, "tilt": 28.32,
        "pole_ra": 299.36, "pole_dec": 43.46,
        "color": [0.26, 0.40, 0.80],
        "description": "Outermost giant planet; Triton orbits retrograde.",
    },
]

SUN = {
    "id": "sun", "name": "Sun", "kind": "star",
    "radius_km": 696340.0, "gm": 1.32712440018e20,
    "rotation_h": 609.12, "tilt": 7.25,
    "pole_ra": 286.13, "pole_dec": 63.87,
    "color": [1.0, 0.93, 0.72],
    "description": "G2V star containing 99.86% of the system mass.",
}

# ---------------------------------------------------------------------------
# Major moons. a is the semi-major axis in km, period in days, radius in km.
# Plane is either "parentEquator" or "ecliptic" (Moon). Epoch phases are
# authored; this is not a satellite ephemeris.
# ---------------------------------------------------------------------------
MOONS = [
    # Earth
    dict(id="moon", name="Moon", parent="earth", a_km=384400.0, e=0.0549,
         i=5.145, plane="ecliptic", radius_km=1737.4, gm=4.9048695e12,
         period_d=27.321661, color=[0.62, 0.62, 0.60],
         description="Tidally locked; the only other world walked on by humans."),
    # Mars
    dict(id="phobos", name="Phobos", parent="mars", a_km=9376.0, e=0.0151,
         i=1.08, radius_km=11.267, gm=7.087e5, period_d=0.318910,
         color=[0.45, 0.42, 0.38], description="Irregular inner moon, spiralling inward."),
    dict(id="deimos", name="Deimos", parent="mars", a_km=23463.0, e=0.0002,
         i=1.79, radius_km=6.2, gm=9.62e4, period_d=1.262440,
         color=[0.48, 0.45, 0.41], description="Small outer moon of Mars."),
    # Jupiter
    dict(id="io", name="Io", parent="jupiter", a_km=421700.0, e=0.0041,
         i=0.05, radius_km=1821.6, gm=5.9599e12, period_d=1.769138,
         color=[0.86, 0.80, 0.42], description="Volcanic moon heated by tidal flexing."),
    dict(id="europa", name="Europa", parent="jupiter", a_km=671034.0, e=0.0090,
         i=0.47, radius_km=1560.8, gm=3.2027e12, period_d=3.551181,
         color=[0.76, 0.72, 0.64], description="Ice shell over a probable subsurface ocean."),
    dict(id="ganymede", name="Ganymede", parent="jupiter", a_km=1070412.0, e=0.0013,
         i=0.20, radius_km=2634.1, gm=9.8878e12, period_d=7.154553,
         color=[0.62, 0.58, 0.52], description="Largest moon in the solar system."),
    dict(id="callisto", name="Callisto", parent="jupiter", a_km=1882709.0, e=0.0074,
         i=0.24, radius_km=2410.3, gm=7.1793e12, period_d=16.689018,
         color=[0.42, 0.39, 0.35], description="Heavily cratered ancient surface."),
    # Saturn
    dict(id="mimas", name="Mimas", parent="saturn", a_km=185539.0, e=0.0196,
         i=1.57, radius_km=198.2, gm=2.5026e10, period_d=0.942422,
         color=[0.68, 0.67, 0.64], description="Small icy moon with the Herschel crater."),
    dict(id="enceladus", name="Enceladus", parent="saturn", a_km=238037.0, e=0.0047,
         i=0.02, radius_km=252.1, gm=7.2104e10, period_d=1.370218,
         color=[0.86, 0.88, 0.88], description="South-polar plumes vent a subsurface ocean."),
    dict(id="tethys", name="Tethys", parent="saturn", a_km=294672.0, e=0.0001,
         i=1.09, radius_km=531.1, gm=4.1213e10, period_d=1.887802,
         color=[0.76, 0.76, 0.74], description="Icy moon with the Odysseus impact basin."),
    dict(id="dione", name="Dione", parent="saturn", a_km=377415.0, e=0.0022,
         i=0.02, radius_km=561.4, gm=7.3105e10, period_d=2.736915,
         color=[0.72, 0.72, 0.71], description="Icy moon with bright tectonic fractures."),
    dict(id="rhea", name="Rhea", parent="saturn", a_km=527068.0, e=0.0013,
         i=0.35, radius_km=763.8, gm=1.5394e11, period_d=4.518212,
         color=[0.70, 0.70, 0.69], description="Saturn's second-largest moon."),
    dict(id="titan", name="Titan", parent="saturn", a_km=1221870.0, e=0.0288,
         i=0.33, radius_km=2574.73, gm=8.9781e12, period_d=15.945421,
         color=[0.80, 0.62, 0.32], description="Thick nitrogen atmosphere and methane lakes."),
    dict(id="iapetus", name="Iapetus", parent="saturn", a_km=3560820.0, e=0.0286,
         i=15.47, radius_km=734.5, gm=1.2051e11, period_d=79.3215,
         color=[0.50, 0.48, 0.44], description="Two-tone surface and a distant inclined orbit."),
    # Uranus
    dict(id="miranda", name="Miranda", parent="uranus", a_km=129390.0, e=0.0013,
         i=4.34, radius_km=235.8, gm=4.4e9, period_d=1.413479,
         color=[0.66, 0.66, 0.66], description="Small moon with extreme fault scarps."),
    dict(id="ariel", name="Ariel", parent="uranus", a_km=190900.0, e=0.0012,
         i=0.26, radius_km=578.9, gm=9.0e10, period_d=2.520379,
         color=[0.72, 0.72, 0.71], description="Brightest and most fractured Uranian moon."),
    dict(id="umbriel", name="Umbriel", parent="uranus", a_km=266000.0, e=0.0039,
         i=0.13, radius_km=584.7, gm=8.5e10, period_d=4.144177,
         color=[0.42, 0.42, 0.41], description="Dark, ancient Uranian moon."),
    dict(id="titania", name="Titania", parent="uranus", a_km=436300.0, e=0.0011,
         i=0.34, radius_km=788.9, gm=2.4e11, period_d=8.705872,
         color=[0.62, 0.62, 0.61], description="Largest moon of Uranus."),
    dict(id="oberon", name="Oberon", parent="uranus", a_km=583500.0, e=0.0014,
         i=0.06, radius_km=761.4, gm=2.0e11, period_d=13.463239,
         color=[0.55, 0.54, 0.52], description="Outer major moon of Uranus."),
    # Neptune
    dict(id="triton", name="Triton", parent="neptune", a_km=354759.0, e=0.0000,
         i=156.885, radius_km=1353.4, gm=1.4276e12, period_d=-5.876854,
         color=[0.78, 0.77, 0.74], description="Retrograde captured moon with nitrogen geysers."),
    dict(id="proteus", name="Proteus", parent="neptune", a_km=117647.0, e=0.0005,
         i=0.52, radius_km=210.0, gm=1.8e9, period_d=1.122315,
         color=[0.40, 0.39, 0.38], description="Irregular, dark inner moon of Neptune."),
    dict(id="nereid", name="Nereid", parent="neptune", a_km=5513400.0, e=0.7507,
         i=7.23, radius_km=170.0, gm=1.0e9, period_d=360.13,
         color=[0.48, 0.47, 0.45], description="Highly eccentric outer moon of Neptune."),
]

# ---------------------------------------------------------------------------
# Six stations. Positions are computed at runtime from the current positions
# of the two bodies, so the stations stay on their Lagrange points.
# ---------------------------------------------------------------------------
STATIONS = [
    dict(id="gateway", name="Gateway Station", primary="earth", secondary="moon",
         point="L1", size_km=2.0,
         description="Earth-Moon L1 transit hub and lunar-surface logistics depot."),
    dict(id="helios_watch", name="Helios Watch", primary="sun", secondary="earth",
         point="L1", size_km=2.5,
         description="Solar observatory and space-weather warning station, 1.5 million km sunward of Earth."),
    dict(id="nyx_anchor", name="Nyx Anchor", primary="sun", secondary="earth",
         point="L2", size_km=2.0,
         description="Deep-space telescope servicing yard and quiet-sector research station."),
    dict(id="ares_junction", name="Ares Junction", primary="sun", secondary="mars",
         point="L1", size_km=2.2,
         description="Mars approach control, propellant cache and transfer rendezvous."),
    dict(id="jovian_yard", name="Jovian Transfer Yard", primary="sun", secondary="jupiter",
         point="L4", size_km=3.5,
         description="Large construction yard in Jupiter's leading Trojan swarm."),
    dict(id="titan_reach", name="Titan Reach", primary="saturn", secondary="titan",
         point="L1", size_km=2.0,
         description="Saturn-system depot at the Titan-Saturn L1 point."),
]


def wrap180(x: float) -> float:
    return (x + 180.0) % 360.0 - 180.0


def planet_elements_at(jd: float, idx: int) -> dict:
    a0, ad, e0, ed, i0, idd, l0, ld, p0, pd, n0, nd = idx
    t = (jd - J2000) / 36525.0
    a = a0 + ad * t
    e = e0 + ed * t
    inc = i0 + idd * t
    mean_lon = l0 + ld * t
    peri_lon = p0 + pd * t
    node = n0 + nd * t
    arg_peri = peri_lon - node
    mean_anomaly = wrap180(mean_lon - peri_lon)
    return dict(a=float(a), e=float(e), i=float(inc),
                raan=float(node), arg_peri=float(arg_peri),
                m0=float(mean_anomaly))


def moon_elements(km: dict, index: int) -> dict:
    a_m = km["a_km"] * 1000.0
    period_s = abs(km["period_d"]) * DAY_S
    mu = 4.0 * math.pi ** 2 * a_m ** 3 / period_s ** 2
    phase = (index * 137.508) % 360.0
    return dict(
        a_m=a_m, e=km["e"], i_deg=km["i"],
        raan_deg=(index * 61.0) % 360.0,
        arg_peri_deg=(index * 29.0) % 360.0,
        mean_anomaly_deg=phase,
        epoch_jd=START_JD, plane=km.get("plane", "parentEquator"),
        gravitational_parameter=mu,
    )


def fetch_asteroids(offline: bool) -> list:
    if offline:
        with open(CACHE, "r", encoding="utf-8") as fh:
            return json.load(fh)

    fields = "full_name,name,pdes,class,diameter,a,e,i,om,w,ma,epoch,GM,rot_per"
    query = urllib.parse.urlencode({
        "fields": fields,
        "sort": "-diameter",
        "limit": "500",
        "sb-kind": "a",
    })
    url = "https://ssd-api.jpl.nasa.gov/sbdb_query.api?" + query
    print("Fetching asteroid data from JPL SBDB ...")
    with urllib.request.urlopen(url, timeout=60) as response:
        payload = json.load(response)
    rows = payload["data"]
    os.makedirs(os.path.dirname(CACHE), exist_ok=True)
    with open(CACHE, "w", encoding="utf-8") as fh:
        json.dump(rows, fh)
    with open(CACHE + ".fields.json", "w", encoding="utf-8") as fh:
        json.dump(payload["fields"], fh, indent=1)
    return rows


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--offline", action="store_true")
    parser.add_argument(
        "--output",
        default=OUT,
        help="write the catalogue here instead of the committed path",
    )
    args = parser.parse_args()
    output = os.path.abspath(args.output)

    bodies = []

    # Sun
    bodies.append(dict(
        id=SUN["id"], name=SUN["name"], kind=SUN["kind"], parent=None,
        radiusM=SUN["radius_km"] * 1000.0, gm=SUN["gm"],
        rotationPeriodS=SUN["rotation_h"] * 3600.0,
        axialTiltDeg=SUN["tilt"],
        poleRaDeg=SUN["pole_ra"], poleDecDeg=SUN["pole_dec"],
        color=SUN["color"], description=SUN["description"],
        orbit=None, lagrange=None,
    ))

    # Planets
    for planet in PLANETS:
        el = planet_elements_at(START_JD, planet["elements"])
        bodies.append(dict(
            id=planet["id"], name=planet["name"], kind="planet", parent="sun",
            radiusM=planet["radius_km"] * 1000.0, gm=planet["gm"],
            rotationPeriodS=planet["rotation_h"] * 3600.0,
            axialTiltDeg=planet["tilt"],
            poleRaDeg=planet["pole_ra"], poleDecDeg=planet["pole_dec"],
            color=planet["color"], description=planet["description"],
            orbit=dict(
                aM=el["a"] * AU_M, e=el["e"], iDeg=el["i"],
                raanDeg=el["raan"], argPeriDeg=el["arg_peri"],
                meanAnomalyDeg=el["m0"], epochJulianDate=START_JD,
                plane="ecliptic",
            ),
            lagrange=None,
        ))

    # Moons
    for index, moon in enumerate(MOONS):
        el = moon_elements(moon, index)
        bodies.append(dict(
            id=moon["id"], name=moon["name"], kind="moon", parent=moon["parent"],
            radiusM=moon["radius_km"] * 1000.0, gm=moon["gm"],
            rotationPeriodS=abs(moon["period_d"]) * DAY_S,
            axialTiltDeg=0.0,
            poleRaDeg=0.0, poleDecDeg=90.0,
            color=moon["color"], description=moon["description"],
            orbit=dict(
                aM=el["a_m"], e=el["e"], iDeg=el["i_deg"],
                raanDeg=el["raan_deg"], argPeriDeg=el["arg_peri_deg"],
                meanAnomalyDeg=el["mean_anomaly_deg"],
                epochJulianDate=el["epoch_jd"], plane=el["plane"],
            ),
            lagrange=None,
        ))

    # Asteroids
    rows = fetch_asteroids(args.offline)
    fields = ["full_name", "name", "pdes", "class", "diameter", "a", "e", "i",
              "om", "w", "ma", "epoch", "GM", "rot_per"]
    asteroids = []
    for row in rows:
        rec = dict(zip(fields, row))
        if rec["class"] != "MBA" or rec["diameter"] is None:
            continue
        a_au = float(rec["a"])
        if not (2.0 <= a_au <= 3.5):
            continue
        asteroids.append(rec)
        if len(asteroids) == 100:
            break
    if len(asteroids) < 100:
        raise SystemExit(f"Only found {len(asteroids)} main-belt asteroids.")

    print(f"Writing {len(asteroids)} main-belt asteroids.")
    # A few asteroid names collide with major-moon names (52 Europa, 85 Io,
    # 106 Dione). Qualify only those with their catalogue number so the HUD
    # and target picker never show two different bodies with one name.
    used_names = {body["name"] for body in bodies}
    for index, ast in enumerate(asteroids):
        name = (ast["name"] or "").strip()
        if not name:
            name = "Asteroid " + str(ast["pdes"])
        elif name in used_names:
            name = f"{ast['pdes']} {name}"
        used_names.add(name)
        diameter_km = float(ast["diameter"])
        radius_m = diameter_km * 500.0
        gm = float(ast["GM"]) * 1e9 if ast["GM"] not in (None, "") else None
        if gm is None:
            # Assume 2.5 g/cm3 for the gravity field. Good enough for a marker.
            density = 2500.0
            mass = density * (4.0 / 3.0) * math.pi * radius_m ** 3
            gm = 6.67430e-11 * mass
        epoch = float(ast["epoch"])
        a_m = float(ast["a"]) * AU_M
        period_s = 2.0 * math.pi * math.sqrt(a_m ** 3 / 1.32712440018e20)
        advance = (START_JD - epoch) * DAY_S
        m0 = float(ast["ma"]) + 360.0 * advance / period_s
        m0 = wrap180(m0)
        rot_h = ast["rot_per"]
        rotation_s = float(rot_h) * 3600.0 if rot_h not in (None, "") else (6.0 + (index * 0.37) % 14.0) * 3600.0
        shade = 0.34 + (index % 7) * 0.025
        bodies.append(dict(
            id=f"ast_{ast['pdes']}", name=name, kind="asteroid", parent="sun",
            radiusM=radius_m, gm=gm,
            rotationPeriodS=rotation_s, axialTiltDeg=(index * 13.0) % 40.0,
            poleRaDeg=(index * 47.0) % 360.0, poleDecDeg=-60.0 + (index * 17.0) % 120.0,
            color=[shade + 0.06, shade + 0.03, shade],
            description=f"Main-belt asteroid, effective diameter {diameter_km:.1f} km.",
            orbit=dict(
                aM=a_m, e=float(ast["e"]), iDeg=float(ast["i"]),
                raanDeg=float(ast["om"]), argPeriDeg=float(ast["w"]),
                meanAnomalyDeg=m0, epochJulianDate=START_JD,
                plane="ecliptic",
            ),
            lagrange=None,
        ))

    # Stations
    for station in STATIONS:
        bodies.append(dict(
            id=station["id"], name=station["name"], kind="station",
            parent=station["secondary"],
            radiusM=station["size_km"] * 500.0, gm=0.0,
            rotationPeriodS=600.0, axialTiltDeg=0.0,
            poleRaDeg=0.0, poleDecDeg=90.0,
            color=[0.72, 0.80, 0.86], description=station["description"],
            orbit=None,
            lagrange=dict(primary=station["primary"], secondary=station["secondary"],
                          point=station["point"]),
        ))

    out = dict(
        name="Time to Explore - Sol",
        epochUtc="2026-11-15T00:00:00Z",
        epochJulianDate=START_JD,
        startJulianDate=START_JD,
        sourceNote=(
            "Planets: JPL approximate positions (Standish & Williams). "
            "Asteroids: JPL SBDB query, 100 largest main-belt objects. "
            "Moons: IAU/JPL physical data with authored epoch phases. "
            "Stations: fictional, positioned at real Lagrange points."
        ),
        bodies=bodies,
    )

    os.makedirs(os.path.dirname(output) or ".", exist_ok=True)
    with open(output, "w", encoding="utf-8") as fh:
        json.dump(out, fh, indent=1, ensure_ascii=False)
    print(f"Wrote {len(bodies)} bodies to {output}")
    print(f"Epoch JD {START_JD} ({out['epochUtc']})")


if __name__ == "__main__":
    main()
