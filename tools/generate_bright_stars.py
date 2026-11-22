#!/usr/bin/env python3
"""Generate the bright-star catalogue that gives the sky its constellations.

The Milky Way band is a model, and the shader's star field is procedural, so
neither contains a real star. This generator carries the missing half: the
brightest stars of the actual sky, at their actual positions, so Orion and the
Plough are where they belong. The shader draws them as a fourth layer.

Source
------
Yale Bright Star Catalogue, 5th Revised Edition (BSC5P, Hoffleit & Warren),
as published by NASA HEASARC, table ``heasarc_bsc5p``:

    https://heasarc.gsfc.nasa.gov/FTP/heasarc/dbase/tdat_files/heasarc_bsc5p.tdat.gz

J2000.0 right ascension and declination in degrees, V magnitude, and B-V colour
index. The table below is every entry with ``V <= MAGNITUDE_LIMIT``, which is
the few hundred stars a dark sky shows without help. Components of a multiple
system that the catalogue lists at the same position are merged into the
brighter one, because a six-arcsecond pair is a single point at the
prototype's resolution.

Regenerating the table
----------------------
The embedded table is the source of truth and needs no network. To rebuild it
at a different cutoff, download the HEASARC table and run

    .venv/bin/python tools/generate_bright_stars.py \\
        --from-bsc heasarc_bsc5p.tdat.gz --magnitude-limit 4.5 --table-out -

which prints a replacement table to paste between the ``TABLE`` markers below.

Output
------
``TimeToExplore/Assets/Resources/Sky/bright_stars.json``. Each star carries its
catalogue position *and* its unit direction in the game's spun ecliptic frame,
rotated by ``tools/skyframe.py`` — the same matrix the band uses, so the stars
and the dust lanes are in one sky. ``x, y, z`` is what the runtime bins into the
lookup texture, and ``ra, dec`` is kept for a reader and a future label.

Usage:
    python3 tools/generate_bright_stars.py            # write the JSON
    python3 tools/generate_bright_stars.py --check    # report, write nothing
"""

from __future__ import annotations

import argparse
import gzip
import json
import math
import os
import re

import numpy as np

from skyframe import equatorial, world_from_equatorial

# Every catalogue entry at or brighter than this V magnitude. Magnitude 4.0 is
# about 500 stars: the figures of the 88 constellations are drawn with stars of
# 1.5 to 3.5, so this is comfortably enough to recognise them, and faint enough
# that the field still reads as a sky rather than a chart.
MAGNITUDE_LIMIT = 4.0

# Two entries closer than this are components of one multiple system that BSC
# lists at the same position. The brighter one is kept. 0.1 degree is nearly
# two screen pixels at the prototype's field of view, so nothing visible is lost.
MERGE_DEGREES = 0.1

# B-V is absent for a couple of entries; a solar-type colour is the least wrong
# guess and the star is faint enough that it does not read as a tint.
DEFAULT_BV = 0.65

TABLE = """
hr|bayer|ra_deg|dec_deg|vmag|bv
2491|Alp CMa|101.28710|-16.71610|-1.46|0.00
2326|Alp Car|95.98790|-52.69580|-0.72|0.15
5340|Alp Boo|213.91540|19.18250|-0.04|1.23
5459|Alp1Cen|219.89960|-60.83530|-0.01|0.71
7001|Alp Lyr|279.23460|38.78360|0.03|0.00
1708|Alp Aur|79.17250|45.99810|0.08|0.80
1713|Bet Ori|78.63460|-8.20170|0.12|-0.03
2943|Alp CMi|114.82540|5.22500|0.38|0.42
472|Alp Eri|24.42870|-57.23670|0.46|-0.16
2061|Alp Ori|88.79290|7.40690|0.50|1.85
5267|Bet Cen|210.95580|-60.37310|0.61|-0.23
7557|Alp Aql|297.69580|8.86830|0.77|0.22
1457|Alp Tau|68.98000|16.50920|0.85|1.54
6134|Alp Sco|247.35170|-26.43190|0.96|1.83
5056|Alp Vir|201.29830|-11.16140|0.98|-0.23
2990|Bet Gem|116.32880|28.02610|1.14|1.00
8728|Alp PsA|344.41290|-29.62220|1.16|0.09
4853|Bet Cru|191.93000|-59.68860|1.25|-0.23
7924|Alp Cyg|310.35790|45.28030|1.25|0.09
4730|Alp1Cru|186.64960|-63.09920|1.33|-0.24
5460|Alp2Cen|219.90040|-60.83560|1.33|0.88
3982|Alp Leo|152.09290|11.96720|1.35|-0.11
2618|Eps CMa|104.65620|-28.97220|1.50|-0.21
4763|Gam Cru|187.79120|-57.11330|1.63|1.59
6527|Lam Sco|263.40210|-37.10390|1.63|-0.22
1790|Gam Ori|81.28290|6.34970|1.64|-0.22
1791|Bet Tau|81.57290|28.60750|1.65|-0.13
3685|Bet Car|138.30000|-69.71720|1.68|0.00
1903|Eps Ori|84.05330|-1.20190|1.70|-0.19
4731|Alp2Cru|186.65210|-63.09940|1.73|-0.26
8425|Alp Gru|332.05830|-46.96110|1.74|-0.13
4905|Eps UMa|193.50710|55.95970|1.77|-0.02
3207|Gam2Vel|122.38330|-47.33670|1.78|-0.22
1017|Alp Per|51.08080|49.86110|1.79|0.48
4301|Alp UMa|165.93210|61.75080|1.79|1.07
2693|Del CMa|107.09790|-26.39330|1.84|0.68
6879|Eps Sgr|276.04290|-34.38470|1.85|-0.03
3307|Eps Car|125.62830|-59.50970|1.86|1.28
5191|Eta UMa|206.88500|49.31330|1.86|-0.19
6553|The Sco|264.33000|-42.99780|1.87|0.40
2088|Bet Aur|89.88210|44.94750|1.90|0.03
6217|Alp TrA|252.16620|-69.02780|1.92|1.44
2421|Gam Gem|99.42790|16.39920|1.93|0.00
7790|Alp Pav|306.41210|-56.73500|1.94|-0.20
3485|Del Vel|131.17580|-54.70830|1.96|0.04
2294|Bet CMa|95.67500|-17.95580|1.98|-0.23
2891|Alp Gem|113.65000|31.88830|1.98|0.03
3748|Alp Hya|141.89670|-8.65860|1.98|1.44
617|Alp Ari|31.79330|23.46250|2.00|1.15
5958||239.87580|25.92030|2.00|0.10
424|Alp UMi|37.95290|89.26420|2.02|0.60
7121|Sig Sgr|283.81630|-26.29670|2.02|-0.22
188|Bet Cet|10.89750|-17.98670|2.04|1.02
1948|Zet Ori|85.18960|-1.94280|2.05|-0.21
15|Alp And|2.09710|29.09060|2.06|-0.11
337|Bet And|17.43290|35.62060|2.06|1.58
2004|Kap Ori|86.93920|-9.66970|2.06|-0.17
5288|The Cen|211.67080|-36.37000|2.06|1.01
5563|Bet UMi|222.67630|74.15560|2.08|1.47
6556|Alp Oph|263.73380|12.56000|2.08|0.15
8636|Bet Gru|340.66710|-46.88470|2.10|1.60
936|Bet Per|47.04210|40.95560|2.12|-0.05
4534|Bet Leo|177.26500|14.57190|2.14|0.09
4819|Gam Cen|190.37920|-48.95970|2.17|-0.01
7796|Gam Cyg|305.55710|40.25670|2.20|0.68
3634|Lam Vel|136.99920|-43.43250|2.21|1.66
168|Alp Cas|10.12710|56.53720|2.23|1.17
1852|Del Ori|83.00170|-0.29920|2.23|-0.22
5793|Alp CrB|233.67210|26.71470|2.23|-0.02
6705|Gam Dra|269.15170|51.48890|2.23|1.52
3165|Zet Pup|120.89620|-40.00330|2.25|-0.26
3699|Iot Car|139.27250|-59.27530|2.25|0.18
603|57Gam1And|30.97500|42.32970|2.26|1.37
21|Bet Cas|2.29460|59.14970|2.27|0.34
5054|Zet UMa|200.98120|54.92530|2.27|0.02
6241|Eps Sco|252.54080|-34.29330|2.29|1.15
5132|Eps Cen|204.97170|-53.46640|2.30|-0.22
5469|Alp Lup|220.48250|-47.38830|2.30|-0.20
5440|Eta Cen|218.87670|-42.15780|2.31|-0.19
5953|Del Sco|240.08330|-22.62170|2.32|-0.12
4295|Bet UMa|165.46040|56.38250|2.37|-0.02
99|Alp Phe|6.57080|-42.30610|2.39|1.09
8308|Eps Peg|326.04670|9.87500|2.39|1.53
6580|Kap Sco|265.62210|-39.03000|2.41|-0.22
8775|Bet Peg|345.94380|28.08280|2.42|1.67
6378|Eta Oph|257.59460|-15.72470|2.43|0.06
4554|Gam UMa|178.45750|53.69470|2.44|0.00
8162|Alp Cep|319.64500|62.58560|2.44|0.22
2827|Eta CMa|111.02380|-29.30310|2.45|-0.08
7949|Eps Cyg|311.55290|33.97030|2.46|1.03
264|Gam Cas|14.17710|60.71670|2.47|-0.15
8781|Alp Peg|346.19040|15.20530|2.49|-0.04
3734|Kap Vel|140.52830|-55.01080|2.50|-0.18
911|Alp Cet|45.57000|4.08970|2.53|1.64
5231|Zet Cen|208.88500|-47.28830|2.55|-0.22
4357|Del Leo|168.52710|20.52360|2.56|0.12
6175|Zet Oph|249.28960|-10.56720|2.56|0.02
1865|Alp Lep|83.18250|-17.82220|2.58|0.21
4662|Gam Crv|183.95170|-17.54190|2.59|-0.11
4621|Del Cen|182.08960|-50.72250|2.60|-0.12
7194|Zet Sgr|285.65290|-29.88030|2.60|0.08
4057|41Gam1Leo|154.99290|19.84170|2.61|1.15
5685|Bet Lib|229.25170|-9.38310|2.61|-0.11
2095|The Aur|89.93040|37.21250|2.62|-0.08
5984|8Bet1Sco|241.35920|-19.80560|2.62|-0.07
553|Bet Ari|28.66000|20.80810|2.64|0.13
1956|Alp Col|84.91210|-34.07420|2.64|-0.12
4786|Bet Crv|188.59670|-23.39670|2.65|0.89
5854|Alp Ser|236.06710|6.42560|2.65|1.17
403|Del Cas|21.45420|60.23530|2.68|0.13
5235|Eta Boo|208.67120|18.39780|2.68|0.58
5571|Bet Lup|224.63290|-43.13390|2.68|-0.22
1577|Iot Aur|74.24830|33.16610|2.69|1.53
4216|Mu Vel|161.69250|-49.42000|2.69|0.90
4798|Alp Mus|189.29580|-69.13560|2.69|-0.20
6508|Ups Sco|262.69080|-37.29580|2.69|-0.22
2773|Pi Pup|109.28580|-37.09750|2.70|1.62
5506|Eps Boo|221.24670|27.07420|2.70|0.97
6859|Del Sgr|275.24870|-29.82810|2.70|1.38
7525|Gam Aql|296.56500|10.61330|2.72|1.52
6056|Del Oph|243.58620|-3.69440|2.74|1.58
6132|Eta Dra|245.99790|61.51420|2.74|0.91
5028|Iot Cen|200.14920|-36.71220|2.75|0.04
5531|9Alp2Lib|222.71960|-16.04170|2.75|0.15
4199|The Car|160.73920|-64.39440|2.76|-0.22
1899|Iot Ori|83.85830|-5.91000|2.77|-0.24
6148|Bet Her|247.55500|21.48970|2.77|0.94
6603|Bet Oph|265.86830|4.56720|2.77|1.16
5776|Gam Lup|233.78540|-41.16690|2.78|-0.20
1666|Bet Eri|76.96250|-5.08640|2.79|0.13
6536|Bet Dra|262.60830|52.30140|2.79|0.98
98|Bet Hyi|6.43790|-77.25420|2.80|0.62
4656|Del Cru|183.78630|-58.74890|2.80|-0.23
3185|Rho Pup|121.88580|-24.30420|2.81|0.43
6212|Zet Her|250.32170|31.60310|2.81|0.65
6913|Lam Sgr|276.99250|-25.42170|2.81|1.04
6165|Tau Sco|248.97080|-28.21610|2.82|-0.25
39|Gam Peg|3.30920|15.18360|2.83|-0.23
4932|Eps Vir|195.54420|10.95920|2.83|0.94
1829|Bet Lep|82.06120|-20.75940|2.84|0.82
1203|Zet Per|58.53290|31.88360|2.85|0.12
5897|Bet TrA|238.78540|-63.43060|2.85|0.29
6461|Bet Ara|261.32500|-55.53000|2.85|1.46
591|Alp Hyi|29.69250|-61.56970|2.86|0.28
8502|Alp Tuc|334.62540|-60.25970|2.86|1.39
1165|Eta Tau|56.87130|24.10500|2.87|-0.09
7528|Del Cyg|296.24370|45.13080|2.87|-0.03
8322|Del Cap|326.76000|-16.12720|2.87|0.29
2286|Mu Gem|95.74000|22.51360|2.88|1.64
2890|Alp Gem|113.65000|31.88860|2.88|0.04
1220|Eps Per|59.46330|40.01030|2.89|-0.18
5671|Gam TrA|229.72750|-68.67940|2.89|0.00
5944|Pi Sco|239.71290|-26.11420|2.89|-0.19
6084|Sig Sco|245.29710|-25.59280|2.89|0.13
7264|Pi Sgr|287.44080|-21.02360|2.89|0.35
2845|Bet CMi|111.78750|8.28940|2.90|-0.09
4915|12Alp2CVn|194.00710|38.31830|2.90|-0.12
8232|Bet Aqr|322.88960|-5.57110|2.91|0.83
915|Gam Per|46.19920|53.50640|2.93|0.70
2553|Tau Pup|102.48420|-50.61470|2.93|1.20
8650|Eta Peg|340.75040|30.22140|2.94|0.86
1231|Gam Eri|59.50750|-13.50860|2.95|1.59
4757|Del Crv|187.46620|-16.51560|2.95|-0.05
6510|Alp Ara|262.96040|-49.87610|2.95|-0.17
8414|Alp Aqr|331.44580|-0.31970|2.96|0.98
2473|Eps Gem|100.98290|25.13110|2.98|1.40
3873|Eps Leo|146.46290|23.77420|2.98|0.80
1605|Eps Aur|75.49210|43.82330|2.99|0.54
6746|10Gam2Sgr|271.45210|-30.42420|2.99|1.00
7235|Zet Aql|286.35250|13.86330|2.99|0.01
622|Bet Tri|32.38580|34.98720|3.00|0.14
1910|Zet Tau|84.41120|21.14250|3.00|-0.19
4630|Eps Crv|182.53120|-22.61970|3.00|1.33
5020|Gam Hya|199.73040|-23.17170|3.00|0.92
1122|Del Per|55.73130|47.78750|3.01|-0.13
3890|Ups Car|146.77540|-65.07190|3.01|0.28
4335|Psi UMa|167.41580|44.49860|3.01|1.14
8353|Gam Gru|328.48210|-37.36500|3.01|-0.12
2282|Zet CMa|95.07830|-30.06330|3.02|-0.19
2653|24Omi2CMa|105.75620|-23.83330|3.02|-0.08
5435|Gam Boo|218.01960|38.30830|3.03|0.19
6615|Iot1Sco|266.89620|-40.12690|3.03|0.51
681|Omi Cet|34.83630|-2.97750|3.04|1.42
5193|Mu Cen|207.40420|-42.47390|3.04|-0.17
4069|Mu UMa|155.58210|41.49940|3.05|1.59
4844|Bet Mus|191.57040|-68.10810|3.05|-0.18
5735|Gam UMi|230.18210|71.83390|3.05|0.05
7310|Del Dra|288.13880|67.66170|3.07|1.00
6247|Mu 1Sco|252.96750|-38.04750|3.08|-0.20
7417|6Bet1Cyg|292.68040|27.95970|3.08|1.13
7776|Bet Cap|305.25290|-14.78140|3.08|0.79
3547|Zet Hya|133.84830|5.94560|3.11|1.00
4232|Nu Hya|162.40620|-16.19360|3.11|1.25
6832|Eta Sgr|274.40670|-36.76170|3.11|1.56
7869|Alp Ind|309.39170|-47.29140|3.11|1.00
2040|Bet Col|87.74000|-35.76830|3.12|1.16
3705|Alp Lyn|140.26370|34.39250|3.13|1.55
3803||142.80540|-57.03440|3.13|1.55
4467|Lam Cen|173.94500|-63.01970|3.13|-0.04
5576|Kap Cen|224.79040|-42.10420|3.13|-0.20
6285|Zet Ara|254.65500|-55.99030|3.13|1.60
3569|Iot UMa|134.80170|48.04170|3.14|0.19
6410|Del Her|258.75790|24.83920|3.14|0.08
6418|Pi Her|258.76170|36.80920|3.16|1.44
1641|Eta Aur|76.62880|41.23440|3.17|-0.18
2451|Nu Pup|99.44040|-43.19610|3.17|-0.11
3775|The UMa|143.21420|51.67720|3.17|0.46
6396|Zet Dra|257.19670|65.71470|3.17|-0.12
7039|Phi Sgr|281.41420|-26.99080|3.17|-0.11
1543|Pi 3Ori|72.46000|6.96140|3.19|0.45
1654|Eps Lep|76.36540|-22.37110|3.19|1.46
5463|Alp Cir|220.62670|-64.97530|3.19|0.24
6299|Kap Oph|254.41710|9.37500|3.20|1.15
8115|Zet Cyg|318.23420|30.22690|3.20|0.99
6630||267.46460|-37.04330|3.21|1.17
8974|Gam Cep|354.83670|77.63250|3.21|1.03
5695|Del Lup|230.34290|-40.64750|3.22|-0.22
7710|The Aql|302.82630|-0.82140|3.23|-0.07
8238|Bet Cep|322.16500|70.56080|3.23|-0.22
897|The1Eri|44.56540|-40.30470|3.24|0.14
1208|Gam Hyi|56.80960|-74.23890|3.24|1.62
6075|Eps Oph|244.58040|-4.69250|3.24|0.96
7178|Gam Lyr|284.73580|32.68940|3.24|-0.05
2878|Sig Pup|112.30750|-43.30140|3.25|1.51
6869|Eta Ser|275.32750|-2.89890|3.26|0.94
165|Del And|9.83210|30.86080|3.27|1.28
1465|Alp Dor|68.49920|-55.04500|3.27|-0.10
2550|Alp Pic|102.04750|-61.94140|3.27|0.21
5287|Pi Hya|211.59290|-26.68250|3.27|1.12
6453|The Oph|260.50250|-24.99940|3.27|-0.22
8709|Del Aqr|343.66250|-15.82080|3.27|0.05
2216|Eta Gem|93.71920|22.50670|3.28|1.60
5603|Sig Lib|226.01750|-25.28190|3.29|1.70
5744|Iot Dra|231.23250|58.96610|3.29|1.16
322|Bet Phe|16.52080|-46.71860|3.31|0.89
1702|Mu Lep|78.23290|-16.20560|3.31|-0.11
4660|Del UMa|183.85670|57.03250|3.31|0.08
4037|Ome Car|153.43420|-70.03810|3.32|-0.08
4140||158.00580|-61.68530|3.32|-0.09
7234|Tau Sgr|286.73500|-27.67060|3.32|1.19
6380|Eta Sco|258.03830|-43.23920|3.33|0.41
3045|Xi Pup|117.32380|-24.85970|3.34|1.24
4359|The Leo|168.56000|15.42940|3.34|-0.01
6462|Gam Ara|261.34830|-56.37750|3.34|-0.13
6698|Nu Oph|269.75670|-9.77360|3.34|0.99
1336|Alp Ret|63.60630|-62.47390|3.35|0.91
8465|Zet Cep|332.71370|58.20110|3.35|1.57
1788|Eta Ori|81.11920|-2.39690|3.36|-0.17
2484|Xi Gem|101.32250|12.89560|3.36|0.43
3323|Omi UMa|127.56630|60.71810|3.36|0.84
7377|Del Aql|291.37460|3.11470|3.36|0.32
5107|Zet Vir|203.67330|-0.59580|3.37|0.11
5708|Eps Lup|230.67040|-44.68940|3.37|-0.18
542|Eps Cas|28.59870|63.67000|3.38|-0.15
3482|Eps Hya|131.69420|6.41890|3.38|0.68
4910|Del Vir|193.90080|3.39750|3.38|1.58
921|Rho Per|46.29420|38.84030|3.39|1.65
1412|78The2Tau|67.16540|15.87080|3.40|0.18
4050||154.27080|-61.33220|3.40|1.54
8634|Zet Peg|340.36540|10.83140|3.40|-0.09
429|Gam Phe|22.09120|-43.31830|3.41|1.57
544|Alp Tri|28.27040|29.57890|3.41|0.49
5190|Nu Cen|207.37630|-41.68780|3.41|-0.22
5649|Zet Lup|228.07120|-52.09920|3.41|0.92
5948|Eta Lup|240.03040|-38.39690|3.41|-0.22
6623|Mu Her|266.61460|27.72060|3.42|0.75
7913|Bet Pav|311.23960|-66.20310|3.42|0.16
7957|Eta Cep|311.32250|61.83890|3.43|0.92
219|Eta Cas|12.27500|57.81580|3.44|0.57
3659||137.74170|-58.96690|3.44|-0.19
4031|Zet Leo|154.17250|23.41720|3.44|0.31
7236|Lam Aql|286.56210|-4.88250|3.44|-0.09
334|Eta Cet|17.14750|-10.18220|3.45|1.16
4033|Lam UMa|154.27420|42.91440|3.45|0.03
7106|Bet Lyr|282.52000|33.36280|3.45|0.00
804|Gam Cet|40.82500|3.23580|3.47|0.09
1239|Lam Tau|60.17000|12.49030|3.47|-0.12
2646|Sig CMa|105.42960|-27.93470|3.47|1.73
3117|Chi Car|119.19460|-52.98220|3.47|-0.18
5681|Del Boo|228.87580|33.31470|3.47|0.95
7635|Gam Sge|299.68920|19.49220|3.47|1.57
4377|Nu UMa|169.61960|33.09420|3.48|1.40
6406|64Alp1Her|258.66210|14.39030|3.48|1.44
8684|Mu Peg|342.50080|24.60170|3.48|0.93
8675|Eps Gru|342.13880|-51.31690|3.49|0.08
509|Tau Cet|26.01710|-15.93750|3.50|0.72
5602|Bet Boo|225.48670|40.39060|3.50|0.97
6897|Alp Tel|276.74330|-45.96830|3.51|-0.17
7150|Xi 2Sgr|284.43250|-21.10670|3.51|1.18
3249|Bet Cnc|124.12880|9.18560|3.52|1.48
3852|Omi Leo|145.28750|9.89220|3.52|0.49
3975|Eta Leo|151.83330|16.76280|3.52|-0.03
8694|Iot Cep|342.42000|66.20060|3.52|1.05
1409|Eps Tau|67.15420|19.18030|3.53|1.01
2777|Del Gem|110.03080|21.98220|3.53|0.34
5881|Mu Ser|237.40500|-3.43030|3.53|-0.04
6220|Eta Her|250.72420|38.92220|3.53|0.92
8450|The Peg|332.55000|6.19780|3.53|0.08
1136|Del Eri|55.81210|-9.76330|3.54|0.92
1879|Lam Ori|83.78460|9.93420|3.54|-0.18
3940|Phi Vel|149.21580|-54.56780|3.54|-0.08
4450|Xi Hya|173.25040|-31.85780|3.54|0.94
6561|Xi Ser|264.39670|-15.39860|3.54|0.26
1998|Zet Lep|86.73870|-14.82190|3.55|0.10
5354|Iot Lup|214.85080|-46.05780|3.55|-0.18
74|Iot Cet|4.85710|-8.82390|3.56|1.22
674|Phi Eri|34.12750|-51.51220|3.56|-0.12
1347|41Ups4Eri|64.47370|-33.79830|3.56|-0.12
4382|Del Crt|169.83540|-14.77860|3.56|1.12
5705|Phi1Lup|230.45170|-36.26140|3.56|1.54
7665|Del Pav|302.18170|-66.18190|3.56|0.76
464|51 And|24.49830|48.62830|3.57|1.28
2985|Kap Gem|116.11170|24.39810|3.57|0.93
6252|Mu 2Sco|253.08380|-38.01750|3.57|-0.21
6927|Chi Dra|275.26420|72.73280|3.57|0.49
7754|6Alp2Cap|304.51380|-12.54470|3.57|0.94
2763|Lam Gem|109.52330|16.54030|3.58|0.11
5429|Rho Boo|217.95750|30.37140|3.58|1.30
5794|Ups Lib|234.25630|-28.13500|3.58|1.38
4700|Eps Cru|185.34000|-60.40110|3.59|1.42
402|The Cet|21.00580|-8.18330|3.60|1.06
1030|Omi Tau|51.20330|9.02890|3.60|0.89
1735|Tau Ori|79.40170|-6.84440|3.60|-0.11
1983|Gam Lep|86.11580|-22.44830|3.60|0.47
2540|The Gem|103.19710|33.96110|3.60|0.10
3594|Kap UMa|135.90620|47.15670|3.60|0.00
3786|Psi Vel|142.67500|-40.46670|3.60|0.36
3017||116.31380|-37.96860|3.61|1.73
3994|Lam Hya|152.64710|-12.35420|3.61|1.01
4540|Bet Vir|177.67380|1.76470|3.61|0.55
437|Eta Psc|22.87080|15.34580|3.62|0.97
3447||130.07330|-52.92190|3.62|-0.18
4923|Del Mus|195.56750|-71.54890|3.62|1.18
6271|Zet2Sco|253.64580|-42.36140|3.62|1.37
6500|Del Ara|262.77460|-60.68390|3.62|-0.10
6582|Eta Pav|266.43330|-64.72390|3.62|1.19
8762|Omi And|345.48040|42.32610|3.62|-0.09
838|41 Ari|42.49580|27.26060|3.63|-0.10
1178|27 Tau|57.29040|24.05330|3.63|-0.09
7882|Bet Del|309.38750|14.59530|3.63|0.44
4520|Lam Mus|176.40170|-66.72860|3.64|0.16
1346|Gam Tau|64.94830|15.62750|3.65|0.99
4825|Gam Vir|190.41500|-1.44940|3.65|0.36
5291|Alp Dra|211.09710|64.37580|3.65|-0.05
7986|Bet Ind|313.70250|-58.45420|3.65|1.25
153|Zet Cas|9.24290|53.89690|3.66|-0.20
5812|Tau Lib|234.66420|-29.77780|3.66|-0.17
6743|The Ara|271.65790|-50.09170|3.66|-0.08
8812|88 Aqr|347.36170|-21.17250|3.66|1.22
3757|23 UMa|142.88210|63.06190|3.67|0.33
5867|Bet Ser|236.54710|15.42190|3.67|0.06
3468|Alp Pyx|130.89790|-33.18640|3.68|-0.18
4826|Gam Vir|190.41500|-1.44940|3.68|
5747|Bet CrB|231.95710|29.10580|3.68|0.28
8278|Gam Cap|325.02290|-16.66220|3.68|0.32
1003|16Tau4Eri|49.87920|-21.75780|3.69|1.62
1552|Pi 4Ori|72.80170|5.60500|3.69|-0.17
3884||146.31170|-62.50780|3.69|1.22
8852|Gam Psc|349.29130|3.28220|3.69|0.92
566|Chi Eri|28.98960|-51.60890|3.70|0.85
1142|17 Tau|56.21880|24.11330|3.70|-0.11
6703|Xi Her|269.44130|29.24780|3.70|0.94
2085|Eta Lep|89.10120|-14.16780|3.71|0.33
4518|Chi UMa|176.51250|47.77940|3.71|1.18
5892|Eps Ser|237.70420|4.47780|3.71|0.15
7602|Bet Aql|298.82830|6.40670|3.71|0.86
1567|Pi 5Ori|73.56290|2.44060|3.72|-0.18
2077|Del Aur|89.88170|54.28470|3.72|1.00
5511|109 Vir|221.56210|1.89280|3.72|-0.01
8079|Xi Cyg|316.23290|43.92780|3.72|1.65
8130|Tau Cyg|318.69790|38.04560|3.72|0.39
539|Zet Cet|27.86500|-10.33500|3.73|1.14
1084|Eps Eri|53.23250|-9.45830|3.73|0.88
3080||118.05420|-40.57580|3.73|1.04
6771|72 Oph|271.83750|9.56390|3.73|0.12
1038|Xi Tau|51.79250|9.73280|3.74|-0.09
8204|Zet Cap|321.66670|-22.41140|3.74|1.00
8698|Lam Aqr|343.15370|-7.57970|3.74|1.64
1612|Zet Aur|75.61960|41.07580|3.75|1.22
3614||136.03880|-47.09780|3.75|1.20
6095|Gam Her|245.48000|19.15310|3.75|0.27
6629|Gam Oph|266.97330|2.70720|3.75|0.04
6688|Xi Dra|268.38210|56.87280|3.75|1.18
8571|Del Cep|337.29290|58.41530|3.75|0.60
834|Eta Per|42.67420|55.89560|3.76|1.68
1373|61Del1Tau|65.73370|17.54250|3.76|0.98
1922|Bet Dor|83.40620|-62.48970|3.76|0.82
6229|Eta Ara|252.44620|-59.04140|3.76|1.57
8254|Nu Oct|325.36870|-77.39000|3.76|1.00
8430|Iot Peg|331.75290|25.34500|3.76|0.44
1135|Nu Per|56.29830|42.57860|3.77|0.42
3347|Bet Vol|126.43420|-66.13690|3.77|1.13
7217|Omi Sgr|286.17080|-21.74170|3.77|1.01
7328|Kap Cyg|289.27580|53.36860|3.77|0.96
7906|Alp Del|309.90960|15.91190|3.77|-0.06
7950|Eps Aqr|311.91920|-9.49580|3.77|0.00
8585|Alp Lac|337.82290|50.28250|3.77|0.01
2736|Gam2Vol|107.18710|-70.49890|3.78|1.04
4257||163.37330|-58.85330|3.78|0.95
2650|Zet Gem|106.02710|20.57030|3.79|0.79
2821|Iot Gem|111.43170|27.79810|3.79|1.03
7420|10Iot2Cyg|292.42620|51.72970|3.79|0.14
7735|31 Cyg|303.40790|46.74140|3.79|1.28
941|Kap Per|47.37420|44.85720|3.80|0.98
3888|Ups UMa|147.74750|59.03860|3.80|0.29
4058|41Gam2Leo|154.99420|19.84060|3.80|
5788|Del Ser|233.70040|10.53750|3.80|0.26
5789|Del Ser|233.70040|10.53920|3.80|0.26
6588|Iot Her|264.86620|46.00640|3.80|-0.18
1931|Sig Ori|84.68670|-2.60000|3.81|-0.24
2035|Del Lep|87.83040|-20.87920|3.81|0.99
4094|Mu Hya|156.52250|-16.83640|3.81|1.48
1464|52Ups2Eri|68.88750|-30.56220|3.82|0.98
3690|38 Lyn|139.71120|36.80250|3.82|0.06
4114||156.96960|-58.73940|3.82|0.31
6149|Lam Oph|247.72830|1.98390|3.82|0.01
7536|Del Sge|296.84710|18.53420|3.82|1.41
8961|Lam And|354.39120|46.45810|3.82|1.01
1131|Omi Per|56.07960|32.28830|3.83|0.05
4247|46 LMi|163.32790|34.21500|3.83|1.04
5248|Phi Cen|209.56790|-42.10080|3.83|-0.21
5470|Alp Aps|221.96500|-79.04470|3.83|1.43
6779|Omi Her|271.88580|28.76250|3.83|-0.03
7582|Eps Dra|297.04330|70.26780|3.83|0.89
1411|77The1Tau|67.14380|15.96220|3.84|0.95
3445||130.15670|-46.64890|3.84|0.71
3571||133.76170|-60.64470|3.84|-0.10
4167||159.32540|-48.22580|3.84|0.30
4434|Lam Dra|172.85080|69.33110|3.84|1.62
5849|Gam CrB|235.68580|26.29560|3.84|0.00
6895|109 Her|275.92460|21.76970|3.84|1.18
8518|Gam Aqr|335.41420|-1.38720|3.84|-0.05
1175|Bet Ret|56.05000|-64.80690|3.85|1.13
2020|Bet Pic|86.82130|-51.06640|3.85|0.17
2296|Del Col|95.52830|-33.43640|3.85|0.88
2749|Ome CMa|108.70290|-26.77280|3.85|-0.17
4023||153.68420|-42.12190|3.85|0.05
4133|Rho Leo|158.20290|9.30670|3.85|-0.14
5933|Gam Ser|239.11330|15.66170|3.85|0.48
6030|Del TrA|243.85960|-63.68560|3.85|1.11
6973|Alp Sct|278.80170|-8.24420|3.85|1.33
1326|Alp Hor|63.50040|-42.29440|3.86|1.10
4802|Tau Cen|189.42580|-48.54110|3.86|0.05
6695|The Her|269.06330|37.25060|3.86|1.35
6812|Mu Sgr|273.44080|-21.05890|3.86|0.23
269|Mu And|14.18830|38.49940|3.87|0.13
963|Alp For|48.01790|-28.98690|3.87|0.52
1149|20 Tau|56.45670|24.36780|3.87|-0.07
1481|53 Eri|69.54500|-14.30390|3.87|1.09
1862|Eps Col|82.80290|-35.47060|3.87|1.14
2580|16Omi1CMa|103.53290|-24.18390|3.87|1.73
4773|Gam Mus|188.11670|-72.13310|3.87|-0.15
4787|Kap Dra|188.37080|69.78830|3.87|-0.13
5249|Ups1Cen|209.67000|-44.80360|3.87|-0.20
5646|Kap1Lup|227.98370|-48.73780|3.87|-0.05
25|Eps Phe|2.35290|-45.74750|3.88|1.03
3665|The Hya|138.59120|2.31420|3.88|-0.06
3905|Mu Leo|148.19080|26.00690|3.88|1.22
5089||202.76120|-39.40750|3.88|1.17
5487|Mu Vir|220.76500|-5.65830|3.88|0.38
5928|Rho Sco|239.22130|-29.21420|3.88|-0.20
874|Eta Eri|44.10710|-8.89810|3.89|1.11
4390|Pi Cen|170.25170|-54.49110|3.89|-0.15
4689|Eta Vir|184.97670|-0.66690|3.89|0.02
6092|Tau Her|244.93500|46.31330|3.89|-0.15
6102|Gam Aps|248.36250|-78.89720|3.89|0.91
7615|Eta Cyg|299.07670|35.08330|3.89|1.02
3314||126.41500|-3.90640|3.90|-0.02
7570|Eta Aql|298.11830|1.00560|3.90|0.89
8820|Iot Gru|347.59000|-45.24670|3.90|1.02
1251|Nu Tau|60.78920|5.98920|3.91|0.03
3487||131.50710|-46.04170|3.91|0.00
3845|Iot Hya|144.96420|-1.14280|3.91|1.32
4337||167.14750|-58.97500|3.91|1.23
4743|Sig Cen|187.01000|-50.23060|3.91|-0.19
5787|Gam Lib|233.88170|-14.78940|3.91|1.01
338|Zet Phe|17.09630|-55.24580|3.92|-0.08
6324|Eps Her|255.07250|30.92640|3.92|-0.01
8131|Alp Equ|318.95580|5.24780|3.92|0.53
1463|Nu Eri|69.07960|-3.35250|3.93|-0.21
2970|Alp Mon|115.31170|-9.55110|3.93|1.02
7340|44Rho1Sgr|290.41830|-17.84720|3.93|0.22
100|Kap Phe|6.55080|-43.68000|3.94|0.17
3461|Del Cnc|131.17120|18.15420|3.94|1.08
4399|Iot Leo|170.98120|10.52920|3.94|0.41
8028|Nu Cyg|314.29330|41.16720|3.94|0.02
440|Del Phe|22.81290|-49.07280|3.95|0.99
854|Tau Per|43.56460|52.76250|3.95|0.74
2429|Nu 2CMa|99.17080|-19.25580|3.95|1.06
3024|Zet Vol|115.45500|-72.60610|3.95|1.04
5055|Zet UMa|200.98500|54.92170|3.95|0.13
5883|Chi Lup|237.73960|-33.62720|3.95|-0.04
8667|Lam Peg|341.63290|23.56560|3.95|1.07
1393|43 Eri|66.00920|-34.01690|3.96|1.49
2120|Eta Col|89.78670|-42.81530|3.96|1.14
2538|Kap CMa|102.46040|-32.50860|3.96|-0.23
2996|3 Pup|115.95210|-28.95470|3.96|0.18
4638|Rho Cen|182.91290|-52.36860|3.96|-0.15
5993|9Ome1Sco|241.70170|-20.66920|3.96|-0.04
7590|Eps Pav|300.14790|-72.91060|3.96|-0.03
2012|Nu Aur|87.87250|39.14860|3.97|1.13
3438|Bet Pyx|130.02580|-35.30830|3.97|0.94
3579||135.16000|41.78280|3.97|0.44
3663||137.81920|-62.31720|3.97|-0.18
6714|67 Oph|270.16130|2.93170|3.97|0.02
7348|Alp Sgr|290.97170|-40.61610|3.97|-0.10
8556|Del1Gru|337.31750|-43.49560|3.97|1.03
8892|98 Aqr|350.74250|-20.10060|3.97|1.10
580|50 Cas|30.85870|72.42140|3.98|-0.01
2227|Gam Mon|93.71380|-6.27470|3.98|1.32
2803|Del Vol|109.20750|-67.95720|3.98|0.79
7751|32 Cyg|303.86790|47.71440|3.98|1.52
8848|Gam Tuc|349.35750|-58.23580|3.99|0.40
585|Ups Cet|30.00120|-21.07780|4.00|1.57
868||43.47040|-49.89030|4.00|2.11
3615|Alp Vol|135.61170|-66.39610|4.00|0.14
4102||156.09880|-74.03170|4.00|0.35
5471||220.49000|-37.79360|4.00|-0.17
"""

_BAYER = (
    "Alp|Bet|Gam|Del|Eps|Zet|Eta|The|Iot|Kap|Lam|Mu|Nu|Xi|Omi|"
    "Pi|Rho|Sig|Tau|Ups|Phi|Chi|Psi|Ome"
)
_NUMBERED_BAYER = re.compile(r"^\s*\d+\s*(?=(?:" + _BAYER + r"))")
_SPACES = re.compile(r"\s+")


def clean_bayer(alt_name: str) -> str:
    """'9Alp CMa' -> 'Alp CMa'; a Flamsteed-only '17    Tau' keeps its number.

    A Bayer designation is a Greek letter and a constellation, two tokens. That
    is what separates it from the ambiguous case: '17 Tau' also has a token
    that reads as a Greek letter, because Taurus shares its abbreviation with
    the letter Tau. Requiring two tokens after the number settles it.
    """
    stripped = _NUMBERED_BAYER.sub("", alt_name)
    if len(_SPACES.sub(" ", stripped).strip().split()) >= 2:
        return _SPACES.sub(" ", stripped).strip()
    return _SPACES.sub(" ", alt_name).strip()


def parse_table(text: str) -> list[dict]:
    """Rows of the embedded table, in catalogue order."""
    rows = []
    for line in text.strip().splitlines():
        line = line.strip()
        if not line or line.startswith("#") or line.startswith("hr|"):
            continue
        hr, bayer, ra, dec, vmag, bv = line.split("|")
        rows.append(
            {
                "hr": int(hr),
                "bayer": bayer,
                "ra": float(ra),
                "dec": float(dec),
                "magnitude": float(vmag),
                "bv": float(bv) if bv else DEFAULT_BV,
            }
        )
    return rows


def merge_duplicates(rows: list[dict]) -> list[dict]:
    """Collapse multiple-system components listed at the same position."""
    kept: list[dict] = []
    for row in sorted(rows, key=lambda r: (r["magnitude"], r["hr"])):
        merged = False
        for existing in kept:
            separation = angular_separation(row, existing)
            if separation < MERGE_DEGREES:
                merged = True
                break
        if not merged:
            kept.append(row)
    return kept


def angular_separation(a: dict, b: dict) -> float:
    """Great-circle separation in degrees between two catalogue positions."""
    ra1, dec1 = math.radians(a["ra"]), math.radians(a["dec"])
    ra2, dec2 = math.radians(b["ra"]), math.radians(b["dec"])
    cos_angle = (
        math.sin(dec1) * math.sin(dec2)
        + math.cos(dec1) * math.cos(dec2) * math.cos(ra1 - ra2)
    )
    return math.degrees(math.acos(max(-1.0, min(1.0, cos_angle))))


def build(limit: float = MAGNITUDE_LIMIT) -> dict:
    rows = [r for r in parse_table(TABLE) if r["magnitude"] <= limit]
    rows = merge_duplicates(rows)
    rows.sort(key=lambda r: (r["magnitude"], r["ra"]))

    stars = []
    for row in rows:
        direction = world_from_equatorial(equatorial(row["ra"], row["dec"]))
        stars.append(
            {
                "hr": row["hr"],
                "bayer": row["bayer"],
                "ra": round(row["ra"], 5),
                "dec": round(row["dec"], 5),
                "magnitude": row["magnitude"],
                "bv": row["bv"],
                "x": round(float(direction[0]), 6),
                "y": round(float(direction[1]), 6),
                "z": round(float(direction[2]), 6),
            }
        )

    return {
        "sourceNote": (
            "Yale Bright Star Catalogue 5th Revised Edition (BSC5P, Hoffleit & "
            "Warren) via NASA HEASARC. J2000.0 positions, V magnitude, B-V. "
            "Positions rotated into the game's ecliptic frame by "
            "tools/skyframe.py, the same frame as the Milky Way band."
        ),
        "epoch": "J2000.0",
        "magnitudeLimit": limit,
        "stars": stars,
    }


# --- Rebuilding the embedded table from the published catalogue -----------


def extract_from_bsc(path: str, limit: float) -> str:
    """Read a HEASARC tdat file and return a replacement table."""
    opener = gzip.open if path.endswith(".gz") else open
    rows = []
    in_data = False
    with opener(path, "rt") as handle:
        for line in handle:
            if line.startswith("<DATA>"):
                in_data = True
                continue
            if not in_data:
                continue
            fields = line.rstrip("\n").split("|")
            if len(fields) != 54:
                continue
            try:
                vmag = float(fields[50])
                ra = float(fields[34])
                dec = float(fields[11])
            except ValueError:
                continue
            if vmag > limit:
                continue
            bv = fields[4]
            rows.append(
                (
                    int(fields[20]),
                    clean_bayer(fields[2]),
                    ra,
                    dec,
                    vmag,
                    float(bv) if bv else None,
                )
            )

    rows.sort(key=lambda r: (r[4], r[2]))
    lines = ["hr|bayer|ra_deg|dec_deg|vmag|bv"]
    for hr, bayer, ra, dec, vmag, bv in rows:
        lines.append(
            "{}|{}|{:.5f}|{:.5f}|{:.2f}|{}".format(
                hr, bayer, ra, dec, vmag, "" if bv is None else "{:.2f}".format(bv)
            )
        )
    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--out",
        default=os.path.join(
            os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
            "TimeToExplore",
            "Assets",
            "Resources",
            "Sky",
            "bright_stars.json",
        ),
        help="output JSON",
    )
    parser.add_argument("--check", action="store_true", help="report and write nothing")
    parser.add_argument(
        "--from-bsc",
        metavar="PATH",
        help="rebuild the embedded table from a HEASARC bsc5p tdat(.gz) file",
    )
    parser.add_argument(
        "--magnitude-limit",
        type=float,
        default=MAGNITUDE_LIMIT,
        help="V magnitude cutoff (default 4.0)",
    )
    parser.add_argument(
        "--table-out",
        metavar="PATH",
        default="-",
        help="where --from-bsc writes the table ('-' for stdout)",
    )
    args = parser.parse_args()

    if args.from_bsc:
        table = extract_from_bsc(args.from_bsc, args.magnitude_limit)
        if args.table_out == "-":
            print(table, end="")
        else:
            with open(args.table_out, "w", encoding="utf-8") as handle:
                handle.write(table)
            print(f"wrote {args.table_out}")
        return 0

    catalogue = build(args.magnitude_limit)
    stars = catalogue["stars"]
    magnitudes = np.array([s["magnitude"] for s in stars])
    print(
        "{} stars to V {:.1f}, magnitude {:.2f} to {:.2f}".format(
            len(stars), args.magnitude_limit, magnitudes.min(), magnitudes.max()
        )
    )
    print(
        "brightest: "
        + ", ".join(s["bayer"] or f"HR {s['hr']}" for s in stars[:5])
        + " · faintest listed: "
        + (stars[-1]["bayer"] or f"HR {stars[-1]['hr']}")
    )

    if args.check:
        return 0

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as handle:
        json.dump(catalogue, handle, indent=1)
        handle.write("\n")
    print(f"wrote {args.out} ({os.path.getsize(args.out) / 1024:.0f} KiB)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
