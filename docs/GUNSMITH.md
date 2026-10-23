# Airsoft — Gunsmith a progrese (návrh)

> Status: **návrh / design draft** (2026-10-23). Není to zatím implementované; slouží jako podklad k rozhodnutí
> a k rozfázování práce. Mapuje reálné airsoftové díly na herní staty a na ekonomiku „hraj → vydělej → vylepši loadout".

## Jádro nápadu

Hráč hraje zápasy, dostává **Credits** (herní peníze) a **XP** (rank). Za XP se **odemyká** (co smíš koupit), za
Credits se **kupuje a tuní** replika z reálných airsoftových dílů. Lepší loadout = výhoda, ale s kompromisy
(vyšší FPS → menší kadence a limity na mapě; HPA → konzistence a vysoká kadence, ale drahé; odstřelovačka →
one-shot, ale pomalá). Témata jsou autenticky airsoftová, ne „sci-fi atachmenty".

Důležitý balanční prvek z reálu: **field limity** — každá mapa má strop FPS a pravidlo **MED** (minimum
engagement distance). CQB (Warehouse) má nízký FPS strop a MED → tam se vyplatí vysoká kadence + bang rule;
otevřená mapa (Town) pustí vysoký FPS a DMR/sniper. Tím jeden „nej" loadout neexistuje.

---

## Platformy (základ repliky)

Base, který kupuješ; určuje, jaké díly jdou tunit a jaký je styl hry.

| Platforma | Styl | Silné | Slabé | Cena |
|---|---|---|---|---|
| **AEG** (elektrická, gearbox) | univerzál | nejhlubší tuning, spolehlivé, levné mags | kadence limituje motor+baterie | nízká–střední |
| **GBBR** (plynová se zpětným rázem) | realistické | zpětný ráz, realistické přebití, zásah | chladne (plyn), nižší kapacita, dražší | střední |
| **HPA** (externí vzduch + engine) | high-performance | konzistentní FPS, extrémní kadence, jemný tuning přes FCU | nutná tlaková láhev (hadice), drahé | vysoká |
| **Spring / bolt** (odstřelovačka) | precizní | vysoká energie, přesnost, tichá | jedna rána, ruční natažení | střední |

---

## Katalog laditelných dílů → herní stat

Staty odkazují na pole v [`WeaponData.cs`](../Assets/Scripts/FPS/WeaponData.cs): `damage`, `fireRate`,
`range`, `muzzleVelocity`, `magSize`, `maxReserve`, `reloadTime`, `recoilUp/recoilSide`, `pelletsPerShot`,
`aimFOV`, `moveSpeedMult`. Díl = **modifikátor** nad základem platformy.

### Pohon / drivetrain (AEG) — hlavně kadence a odezva

| Díl | Varianty | Co dělá reálně | Herní stat |
|---|---|---|---|
| **Motor** | high-torque / vyvážený / high-speed | točivý moment vs. otáčky; utáhne silnější pružinu nebo zvýší ROF | `fireRate`, odezva spouště; high-torque povolí silnější spring |
| **Převody (gears)** | torque-up / standard / speed (poměr) | mění ROF vs. schopnost utáhnout spring | `fireRate` ↔ max povolený spring |
| **Baterie** | LiPo 7.4 V / 11.1 V, C-rating | napětí a proud → otáčky motoru, odezva | `fireRate`, odezva; 11.1 V bez FCU = opotřebení |
| **FCU / MOSFET** | GATE Aster/Titan, Perun | chrání kontakty, aktivní brzda, programovatelné režimy | odemyká burst/binary, přesnější kadence, **počítadlo nábojů**, lepší trigger response |
| **Kabeláž / konektory** | low-resistance, Deans | nižší odpor → víc proudu | malý bonus k odezvě/ROF |

### Komprese / power — hlavně FPS (= damage a range)

| Díl | Varianty | Co dělá | Herní stat |
|---|---|---|---|
| **Hlavní pružina (spring)** | M90–M170 | určuje FPS/energii střely | `muzzleVelocity` → `damage`, `range` |
| **Píst + hlava pístu** | ported / celokov | komprese a spolehlivost | konzistence FPS, spolehlivost (viz opotřebení) |
| **Válec + hlava válce** | objemově laděný k délce hlavně | množství vzduchu k hlavni | efektivita FPS při dané hlavni |
| **Tryska (nozzle)** | délka dle gearboxu | těsnění a feeding | konzistence, spolehlivost feedingu |
| **Vedení pružiny (bearing spring guide)** | ložiskové | zabrání kroucení pružiny | menší rozptyl FPS |

### Přesnost / accuracy — hlavně range a rozptyl

| Díl | Varianty | Co dělá | Herní stat |
|---|---|---|---|
| **Hop-up jednotka** | pákový / rotační | aplikuje rotaci (backspin) | základ efektivního dostřelu |
| **Hop-up guma (bucking)** | tvrdost 50–70° | rotace a těsnění | `range`, konzistence |
| **Nub / flat-hop / R-hop** | nub, flat, R-hop | rovnoměrnost backspinu | vyšší efektivní `range`, menší rozptyl |
| **Vnitřní hlaveň** | délka + bore (6.01–6.05 mm tightbore) | delší/užší = přesnější, ale méně odpustná | `range`/rozptyl ↔ efektivita vzduchu; delší = pomalejší handling |
| **BB (hmotnost)** | 0.20–0.45 g | těžší = stabilnější ve větru, méra doletu | ammo volba: `range`/stabilita ↔ FPS; spotřební |

### Spolehlivost / elektronika

| Díl | Varianty | Co dělá | Herní stat |
|---|---|---|---|
| **Pouzdra/ložiska (bushings/bearings)** | 7/8 mm, kuličková | tření a životnost gearboxu | spolehlivost, drobně efektivita |
| **Shimming** | tuning | hladký chod převodů | konzistence (laditelné zdarma u „gunsmithe") |
| **FCU režimy** | burst / binary / active brake | střelecké režimy | odemyká `isAutomatic`/burst, počítadlo |

### Externí díly / atachmenty

| Díl | Varianty | Herní stat |
|---|---|---|
| **Optika** | mířidla / red dot / holo / LPVO / scope + magnifier | `aimFOV` (zoom), rychlost zacílení |
| **Vnější hlaveň / tlumič** | délka, suppressor | pojme delší vnitřní hlaveň (range); tlumič ztlumí zvuk výstřelu pro ostatní |
| **Pažba / grip** | nastavitelná, přední grip | `moveSpeedMult`, stabilita (recoil) |
| **Zásobníky** | mid-cap / hi-cap / drum | `magSize`/`maxReserve` ↔ realističnost (hi-cap rachotí); rychlost přebití |

### GBBR specifické

| Díl | Co dělá | Herní stat |
|---|---|---|
| **Plyn** | green gas / CO2 / HPA tap | CO2 = vyšší FPS/chladu odolnější | `muzzleVelocity`, výkon „za chladu" |
| **Tryska / ventil / nozzle** | cyklování | spolehlivost cyklu, `fireRate` |
| **Závěr + buffer (bolt/buffer weight)** | zpětný ráz | `recoilUp/Side` (pocit), rychlost cyklu |
| **Kladívková pružina** | timing | konzistence |

### HPA specifické

| Díl | Co dělá | Herní stat |
|---|---|---|
| **Engine** | PolarStar F2/Jack, Wolverine Inferno/SMP | jádro — základní profil ROF/efektivity |
| **Regulátor** | tlak vzduchu | `muzzleVelocity` (FPS) přesně a konzistentně |
| **FCU** | dwell, ROF, burst, Bluetooth | `fireRate` (i extrémní), režimy, dwell → FPS jemně |
| **Tryska / solenoid** | forward-biased, closed-bolt | odezva, konzistence |
| **Tlaková láhev (tank/reg)** | objem | „výdrž" než dojde vzduch (pokud bys chtěl zdroj-management) |

### Spring / sniper specifické

| Díl | Herní stat |
|---|---|
| **Spring + válec + píst** | `muzzleVelocity`/`damage` (one-shot potenciál) |
| **VSR hop + bucking + vnitřní hlaveň** | `range`, rozptyl (precizní) |
| **Trigger sear / pružina spouště** | konzistence, rychlost druhé rány |

---

## Ekonomika a progrese

- **Měna:** `Credits` (herní peníze). **XP** zvlášť → **rank** (brána: co smíš koupit).
- **Výdělek za zápas:** základ za čas + bonus za kill / objektiv (vlajka, plant, defuse, revive, extrakce) + bonus za
  výhru + série. Vyšší obtížnost botů / víc hráčů = víc Credits. (Vzorec k vyladění.)
- **Shop:** base platformy → díly → atachmenty → spotřební (plyn, baterie, BB) *volitelně*.
- **Loadout:** primární + sekundární + gadget (např. kouřák z Fáze 4) + volba BB + nainstalované interní díly. Víc
  uložených loadoutů (presetů).
- **Field limity (balanc):** každá mapa má FPS strop + MED. Build nad strop je na té mapě zašedlý / omezený. Nutí to
  mít víc buildů (CQB vs. pole), ne jeden king.
- **Volitelná hloubka (později):**
  - **Opotřebení/spolehlivost:** přepálený build (11.1 V bez FCU, moc silný spring na slabý motor) má šanci na
    „zasek" (krátká pauza) — motivace kupovat kvalitní díly a „gunsmithing" (shim/maintenance zdarma).
  - **Spotřební zdroje:** plyn/baterie/BB jako průběžný náklad (riziko „pay-to-grind", spíš opatrně).

---

## Jak to sedí na stávající kód

- **`WeaponData.cs`** = základní staty platformy. Přidat **vrstvu dílů**: build = base + součet modifikátorů dílů →
  spočítá výsledný `WeaponData` za běhu (clamp na field limit mapy). Gun Game ignoruje (drží fixní sadu).
- **`PlayerProfile.cs`** (už má XP, unlocky, staty, lokální JSON) rozšířit o `credits`, vlastněné díly/zbraně a
  uložené loadouty. **Pozor:** profil je lokální a ručně editovatelný (píšeš to sám v README) → peníze/díly jdou
  cheatnout. Pro integritu by chtělo **serverové ověření loadoutu** (host odmítne build nad limit / neodemčený) a
  ideálně cloud save (UGS, které už používáš na Relay). Pro školní projekt lokálně stačí, ale limit/odemčení ať
  validuje server.
- **Lobby:** nová obrazovka **GUNSMITH** vedle `CUSTOMIZE` ([`CharacterCustomizerUI.cs`](../Assets/Scripts/FPS/CharacterCustomizerUI.cs)
  je vzor pro 3D náhled + výběr) — záložky Platforma / Interní / Atachmenty / BB, s live přepočtem statů a cenou.
- **`WeaponController.cs`** dostane výsledný `WeaponData` z loadoutu (dnes bere `WeaponData.GetAllWeapons()`), server
  ho ověří (podobně jako už ověřuje zbraň/kadenci/munici).
- **Sync:** loadout se musí poslat ostatním (model/atachmenty pro vizuál) jako dnes vzhled postavy; staty drží a
  ověřuje server.

---

## Rozsah / návrh fází

Je to velká featura. Rozumné pořadí:

1. **Ekonomika MVP:** Credits + XP výdělek za zápas, uložení do profilu, HUD/č výsledky to ukážou. (Bez nákupů.)
2. **Shop + vlastnictví zbraní:** koupě base platforem, loadout ze zakoupených (bez interního tuningu). Serverové
   ověření „vlastníš/odemčeno".
3. **Interní tuning (stat vrstva):** spring/motor/gears/hop/barrel/BB → přepočet `WeaponData`, field limity mapy.
4. **Atachmenty + GBBR/HPA/sniper platformy** s jejich specifiky.
5. **Volitelně:** opotřebení/spolehlivost, spotřební zdroje, cloud save.

---

## Zdroje (rešerše dílů)

- Fox Airsoft — [AEG internal parts](https://www.foxairsoft.com/internal-parts/), [pistons](https://www.foxairsoft.com/piston/)
- Evike — [AEG Encyclopedia: what every part does](https://evike.com/articles/86_The-AEG-Encyclopedia-What-Every-Part-in-Your-AEG-Does), [8 best AEG upgrades](https://evike.com/articles/124_8-Best-AEG-Upgrades-for-Your-Airsoft-Build/), [hop-up & buckings](https://www.evike.com/articles/14_The-Secret-to-Accuracy-Best-Hopup-Buckings/), [HPA explained](https://evike.com/articles/117_HPA-in-Airsoft-What-It-Is-Why-Players-Love-It-and-What-You-Need/)
- PolarStar F1 HPA systém (Evike produktová stránka): <https://www.evike.com/products/61419/>
