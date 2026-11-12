# Airsoft — Vývojový deník (DPM)

Multiplayerová FPS aréna. Unity 6.6 (URP), Netcode for GameObjects 2.13.3.

## Výstup 1 — Návrh a příprava (programátorská část, Denis Řezníček)

### Technický základ
- Projekt v Unity 6.6, render pipeline URP.
- Balíčky: Netcode for GameObjects, Unity Transport, Multiplayer SDK (Relay), Input System, AI Navigation.
- Verzování: Git (+ .gitignore pro Unity, netrackuje se Library/Temp/buildy).

### Herní jádro
- FPS controller (pohyb, sprint, crouch, skok), kamera.
- Střelba (hitscan) + 10 zbraní s rozdílnými statistikami.
- AI nepřátelé na NavMeshi + vlnový spawner (režim přežití).
- Health systém se síťovou synchronizací a respawnem.

### Síť / multiplayer
- Model listen-server (host = server), NetworkManager + registrované prefaby.
- LAN host/join přes IP (port 7777).
- Online přes Unity Relay — připojení krátkým kódem bez port-forwardingu.
- Lobby/čekárna: seznam hráčů, jména, ready-up, výběr režimu (vlastní řešení přes CustomMessagingManager).
- Režimy: Co-op Wave Survival a Free-for-All Deathmatch.
- HUD: zdraví, munice, zaměřovač, lišta zbraní, killfeed (styl CS2), scoreboard (Tab).

### Hlavní skripty (Assets/Scripts/FPS/)
MatchNet.cs, NetworkUI.cs, PlayerSpawner.cs, FPSController.cs, WeaponController.cs,
WeaponData.cs, Health.cs, EnemyAI.cs, EnemySpawner.cs, HUDManager.cs, WorldHealthBar.cs,
AutoConnect.cs, NetDiag.cs.

## 2026-10-19 — Team Deathmatch a Gun Game
- Týmy (automatické rozdělení), v Team Deathmatch bez friendly fire, společné skóre týmu, scoreboard v barvách týmů.
- Gun Game: každý kill posune hráče na další z 10 zbraní, vyhrává ten, kdo projde všechny.
- V lobby se režim přepíná klávesou M (4 režimy), HUD banner a scoreboard podle režimu.

## 2026-10-21 — Low-poly vzhled, postavy, nové zbraně, nová mapa
- Staženy CC0 low-poly balíčky od Quaternius (postavy, zbraně, budovy, ulice, příroda, rekvizity).
- Lobby: editor postavy (tělo, čepice, barva čepice, tričko, kalhoty, barva kůže, vlasy) s 3D náhledem; vzhled se ukládá a synchronizuje všem hráčům.
- Hráči i nepřátelé mají animované low-poly postavy (idle/chůze/běh/smrt), zbraň v ruce; v TDM tričko podle týmu.
- 10 nových zbraní s low-poly modely (Pistol, Revolver, Ray Gun, Pulse SMG, P90, AK-47, Pulse Rifle, M870, Lightning Gun, AWP).
- Nová mapa "město": křižovatka se silnicemi a chodníky, domy, kontejnerový sklad, park, auta a zátarasy jako krytí, panorama budov, obloha, mlha, post-processing.

## 2026-10-21 — Přejmenování Arbes → Airsoft
- Projekt přejmenován v kódu, assetech, scénách i nastavení: namespace `AirsoftFPS`, scény `AirsoftLobby` a `AirsoftArena`, menu *Tools → Airsoft*, název produktu *Airsoft*.

## 2026-10-21 — Nové UI/UX
- Návrh rozhraní v Claude Design (design canvas: https://claude.ai/artifact/H16xM5KTXvKWrjhFWzd41d), vizuální styl „field-tactical“, písma Barlow, Barlow Condensed a JetBrains Mono.
- Implementace v uGUI: `UIKit` (barvy, písma, procedurálně kreslené rámečky a ikony, stavebnice tlačítek, posuvníků a přepínačů), referenční rozlišení 1280×720.
- Menu se záložkami Online / LAN, obrazovka připojování (kroky a průběh, při chybě karta s opakováním).
- Lobby: karty režimů, 8 slotů hráčů s odznaky, ready (klávesa R), START MATCH jen pro hosta, kód hry s kopírováním, 3D náhled postavy.
- Editor postavy jako celoobrazovkové okno. Nastavení (Controls, Video, Audio, Gameplay) se ukládá do PlayerPrefs.
- HUD podle režimu, killfeed, scoreboard (Tab), obrazovka smrti, pauza (zápas běží dál, jen se zablokuje ovládání), výsledky zápasu.
- Menu *Tools → Airsoft → Rebuild UI Only* vymění staré UI ve scénách za nové bez přegenerování mapy.

## 2026-10-21 — Automatické buildy (GitHub Actions)
- Workflow `.github/workflows/build.yml`: GameCI `unity-builder` sestaví build pro macOS a Windows na Linux runneru a uloží ho jako artefakt.
- Pokud je nastavené itch.io, nahraje buildy přes butler do kanálů `mac` a `windows`. Spouští se ručně nebo pushnutím tagu `v*`.
- Česky psaný návod v `docs/BUILD.md` (Unity licence, secrety, stránka na itch.io, tipy pro hráče).

## 2026-10-21 — Časový limit zápasu
- Host v lobby volí limit Off / 5 / 10 / 15 min (výchozí 10, pamatuje si ho), klienti ho jen vidí. V Co-op limit není.
- Server spustí hodiny po načtení mapy; po vypršení vyhodnotí: FFA nejvíc killů, Gun Game level → killy → méně smrtí, týmové režimy skóre týmu. Přesná shoda = remíza (DRAW).
- HUD: hodiny mm:ss pod widgetem režimu, posledních 30 s oranžově, posledních 10 s červeně. Scoreboard a pauza ukazují zbývající čas.

## 2026-10-21 — Týmové pásky, brýle a masky
- V TDM si hráč nechává vlastní tričko a nosí na obou pažích pásky v barvě týmu, navázané na kosti postavy (hýbou se s animací).
- Nový slot ochrany očí: Goggles, Shooting Glasses, Full Face Mask, Mesh Mask (sestavené z primitiv na kosti hlavy).
- Starší uložené vzhledy se načtou bez brýlí, synchronizace přes MatchNet se nezměnila.
- Oprava: lidské postavy byly kvůli roztaženým chodidlům modelů natočené asi o 19° vedle směru pohledu; natočení se teď počítá podle linie ramen.

## 2026-10-21 — Killcam a minimapa
- Killcam: po vyřazení se kamera asi na 2 s otočí na toho, kdo hráče vyřadil (hráč, nebo AI nepřítel v Co-op), potom obrazovka smrti s „Eliminated by …“, zbraní a zbývajícím HP útočníka.
- Údaje o útočníkovi posílá server jen oběti a skládá je z vlastního záznamu zásahů, takže je klient nemůže podvrhnout.
- Minimapa vlevo nahoře: statická část mapy se jednou vyfotí shora ortografickou kamerou, pod ní se otáčí šipka hráče. Spoluhráči jsou tečky, nepřátelé se nezobrazují.
- V *Settings → Gameplay* lze killcam a minimapu vypnout a minimapu přepnout na „sever nahoře“.

## 2026-10-21 — Kuličky (BB) a ověřování zásahů na serveru
- Server ověřuje zásahy: klient hlásí jen co trefil (cíl, číslo výstřelu, zbraň, počátek, bod zásahu). Poškození bere server ze svých `WeaponData`, hlídá kadenci zbraně, počet zásahů na výstřel, dosah, polohu cíle a přímou viditelnost.
- Místo okamžitého zásahu (hitscan) letí skutečné kuličky: úsťová rychlost, gravitace, hop-up (vztlak), odpor vzduchu, simulace s pevným krokem 60 Hz (`BBBallistics`, `BBProjectile`).
- Rozptyl je deterministický ze seedu, takže všichni klienti vidí stejné kuličky. Server let přepočítá a zásah uzná, jen když leží na dráze kuličky bez zdi před ním a uplynul čas letu.
- Pokusy o podvržený zásah (neznámý výstřel, zásah hned na 35 m, mimo dráhu, duplicitní) server odmítne (v editoru a debug buildu je zaloguje).

## 2026-10-21 — Airsoftové repliky místo sci-fi zbraní
- 10 slotů (klávesy 1–0 = pořadí v Gun Game): 1911 GBB pistole, CO2 revolver, Uzi GBB (plynový samopal), P90 AEG, pružinová brokovnice M870 (3 kuličky na výstřel), AK AEG, RPK podpůrná AEG (300ranový box), M14 DMR (poloautomat), pružinová opakovačka (mířidla), L96 pružinová odstřelovačka.
- Statistiky v airsoftových jednotkách: úsťová rychlost 85–150 m/s, mid-cap/hi-cap zásobníky, semi/auto, vyvážené TTK.
- Nové low-poly modely (Quaternius CC0, převedeno z .blend do FBX): AK47, Uzi, BoltRifle; sci-fi modely odstraněny. RPK používá model AK (delší).

## 2026-10-21 — Týmové spawny a ochrana po spawnu
- Komponenta `SpawnPoint` (tým 0 = kdokoli, 1 = Červení, 2 = Modří). Červení se rodí na severu, Modří na jihu.
- Ve všech režimech se vybírá bod nejdál od živých nepřátel, obsazené body se přeskočí.
- Spawny na koncích silnic přesunuty za krytí podle toho, z jak velké části mapy je bod vidět. Menu *Tools → Airsoft → Check Spawn Points* vypíše report.
- Ochrana po spawnu: 3 s nezranitelnost, skončí prvním výstřelem. Hráč vidí odpočet, ostatní kolem něj průsvitný obal.
- Respawn teleportuje hráče na straně vlastníka (NetworkTransform je owner-authoritative), takže se vzdálení klienti opravdu přesunou.

## 2026-10-21 — Profil hráče: statistiky, XP, odemykání
- Lokální profil (JSON v `persistentDataPath`): zápasy, výhry, killy, smrti, zásahy, nejlepší série, oblíbená zbraň, XP a level.
- XP: kill 20, nepřítel v Co-op 10, zásah 3, dohraný zápas 50, výhra 100. Level N → N+1 stojí 150 + 75·(N−1) XP.
- Čepice, brýle/masky a barvy se odemykají s levelem (v editoru postavy zámek s levelem). Vzhled uložený před zavedením profilu zůstává použitelný.
- Menu: čip s profilem a panel statistik. Výsledky: karta XP s rozpisem, animovaným pruhem a oznámením nového levelu.
- Bez změn v síťovém protokolu, počítá se z dat, která klient už dostává.

## 2026-10-21 — Režim Milsim (airsoftové pravidlo HIT)
- Týmový režim Red vs Blue: jeden zásah kuličkou = HIT místo ubírání HP. Zasažený hráč zvedne ruku (vidí to všichni), nemůže střílet, nejde znovu zasáhnout a kuličky jím prolétají.
- Musí dojít do zóny svého týmu, po 3 s v zóně je zpět ve hře. HUD ukazuje směr a vzdálenost k základně.
- Každý zásah = bod pro tým střelce, vyhrává první s 30 body (časový limit platí dál). Friendly fire vypnutý, killfeed „X hit Y“.
- Kontroly „je to TDM?“ nahradily pomocné `TeamMode` / `IsTeamMode`, aby šly přidávat další týmové režimy.
- Nová komponenta `MapLayout` pro body jednotlivých režimů na mapě (základny, kontrolní body, …).

## 2026-10-22 — Boti v PvP režimech
- Host v lobby zapne doplnění boty (Off / do 4 / do 8 hráčů) a obtížnost (Easy / Normal / Hard). Boti odcházejí, když se připojí hráči, a vrací se, když hráči odejdou.
- Bot je AI voják (`BotBrain`, běží na serveru) na NavMeshi: prochází mapu, útočí na nejbližšího viditelného soupeře (respektuje týmy), střílí skutečné kuličky, přebíjí, uhýbá do stran, respawnuje se se spawn ochranou. V Milsim zvedne ruku a jde na základnu.
- Obtížnost mění reakční dobu, rozptyl, přesnost, předsazení a vnímání okolí.
- Boti hrají jen ve FFA, TDM, Gun Game a Milsim. Killfeed, scoreboard, výsledky, killcam i minimapa s nimi počítají. Co-op beze změny.

## 2026-10-22 — Objektivové režimy: Capture the Flag a Domination
- Rámec `ObjectiveMode` (`Assets/Scripts/FPS/Objectives`): pravidla běží na serveru, klienti dostávají stav přes pojmenované zprávy.
- Capture the Flag: vlajky na základnách, nositel nemůže sprintovat, bod jen když je vlastní vlajka doma, při smrti nebo HIT vlajka spadne, po 20 s se sama vrátí. Vyhrává 3 doručení.
- Domination: body A, B, C (poloměr 6 m), obsadí se stáním uvnitř, se soupeřem uvnitř je bod sporný. Každý držený bod dává 1 bod za 2 s, cíl 200.
- Lobby: karta režimu zešedne s důvodem, pokud mapa nemá potřebné body (`MapManifest`). HUD widget podle režimu, značky ve světě, oznámení.
- Body na mapě Town přidává `ObjectiveMapSetup`. Kontroly v editoru: `ObjectiveLogicCheck` (pravidla bez sítě) a `ObjectivePlayCheck` (host v play módu).

## 2026-10-22 — Mapa Warehouse a hlasování o mapě
- Druhá mapa *Warehouse* (60 × 60 m, boj na krátkou vzdálenost): 7 m vysoká hala s regály, kontejner, palety a vysokozdvižné vozíky, kancelářský blok (chodba a šest místností), nakládací dvory na severu (Červení) a jihu (Modří). Zrcadlově symetrická.
- Celou ji staví skript `AirsoftWarehouseBuilder` (*Tools → Airsoft → Build Warehouse Map*): prostředí, 8 týmových spawnů, 6 spawnů nepřátel, NavMesh a body pro všechny režimy.
- Hlasování v lobby: karty map s náhledem, jeden hlas na hráče, vyhrává většina, při shodě rozhoduje hlas hosta, host může mapu zamknout.
- Náhledy map shora (`Resources/MapPreviews`). Střechy a stropy se v minimapě a náhledech skrývají, takže jsou vidět místnosti.
- Oprava: postavy hráčů se při návratu do lobby odstraní, další zápas všechny spawne znovu na nové mapě.

## 2026-10-22 — Režim Medic
- Týmový režim na pravidle HIT: zasažený hráč padne na místě a 45 s „krvácí“. Medik ze stejného týmu ho oživí držením E po dobu 4 s.
- Vykrvácení nebo vzdání se (držet G) = návrat na základnu jako v Milsim a ztráta tiketu.
- 1 medik na každou započatou čtveřici hráčů (přednost mají dobrovolníci z lobby), bílá páska s červeným křížem.
- 25 tiketů pro tým 4 hráčů (škáluje se podle velikosti). Tým s 0 tikety a všemi hráči vyřazenými prohrává, při vypršení času vyhrává tým s víc tikety. Režim je bez botů.

## 2026-10-22 — Režimy Bomb a VIP Escort
- Kolové režimy (`RoundMode`): příprava (zmrazení, všichni na své straně, doplněná munice) → kolo s časem → výsledek. Role se každé kolo prohodí, vyhrané kolo = bod. Místo časového limitu zápasu se hraje na kola.
- Bomb: náhodný útočník nese bombu (při smrti spadne, sebrat ji může kterýkoli útočník), položí ji na A nebo B (držet E 4 s), rozbuška 40 s, obránci ji zneškodní (držet E 6 s). V kole se nerespawnuje, vyřazení sledují spoluhráče. Vyhrává 4 kola.
- VIP Escort: náhodný člen týmu je VIP (jen pistole 1911 a revolver, bez sprintu, pomalejší) a musí dojít k evakuaci (držet E 3 s). Lovci vyhrají, když VIP padne nebo vyprší čas. Vyhrává 3 kola.
- Interakce klávesou E s kruhovým průběhem, widget kola, 3D písmena míst. Na mapu Town přibyla místa A/B a start a cíl VIP.

## 2026-10-22 — Opravy před vydáním
- **Klienti se nemohli spawnout** („NetworkPrefab could not be found“): po přegenerování scén přes `AirsoftSceneBuilder` byl seznam síťových prefabů v lobby prázdný. Builder je registroval přes `NetworkConfig.Prefabs.Add()`, což je seznam jen za běhu a do scény se neuloží. Host mohl spawnovat, klient ne. Lobby teď znovu odkazuje na `Assets/DefaultNetworkPrefabs.asset` a builder ho udržuje.
- **Plná fronta příjmu** v Unity Transport („Receive queue is full“): kuličky, boti a stav objektivů přeplnili výchozí frontu. Fronta paketů zvětšena ze 128 na 512.
- LAN host na obsazeném portu 7777 (např. druhá spuštěná hra na stejném počítači) ukáže chybu s možností opakovat, místo prázdné čekárny.
- Modely mapy se importují s čitelnými meshi, protože `EnemySpawner` staví NavMesh za běhu i v buildu. Jednorázový reimport se nespouští v klonech Multiplayer Play Mode (jsou jen pro čtení).
- MatchNet: žádná výjimka při vypínání hosta, zrušení čekajícího návratu do lobby po ukončení, hráč připojený do běžícího týmového zápasu jde do menšího týmu.

## 2026-10-22 — Vydání v0.2.0 na itch.io
- Buildy pro macOS (Universal) a Windows (64-bit) sestaveny lokálně v Unity 6000.6.0f1 v batch módu z commitu `a8c552c` a nahrány nástrojem butler do kanálů `mac` a `windows`.
- Stránka hry: https://rezdenssps.itch.io/airsoft
- Workflow GitHub Actions zatím neběžel (chybí secrety s Unity licencí), v repu zatím není tag `v0.2.0`.

## 2026-10-22 — Zvuk a hudba
- Hra měla nulový zvuk; teď má ~60 zvuků (130 variant) + menu hudbu a dva ambienty. Všechny jsou vlastní, vygenerované procedurálně skriptem `tools/audio/gen_airsoft_audio.py` (syntéza v Pythonu bez vzorků), licence CC0.
- Zbraně: airsoftové výstřely podle typu (AEG „thwack“ s převodovkou, GBB pistole/Uzi, CO2 revolver, pružinová brokovnice s pumpou, pružinová opakovačka a hlasitější L96 se závěrem), přebití (zásobník ven/dovnitř, závěr), cvaknutí naprázdno, výměna zbraně, „tik“ zásahu a potvrzení eliminace.
- Dopady kuliček podle povrchu (beton s odskoky, plech, dřevo, sklo, tráva/pytle, oblečení) a kroky podle povrchu (chůze/sprint/plížení), skok a dopad. Odvozeno z pohybu na každém klientovi, žádné pakety navíc; výstřely ostatních jdou přes existující RPC.
- Milsim/Medic: hráč po zásahu zavolá „HIT!“ (syntetický hlas, každý hráč má svůj).
- Cíle: vlajka (sebrání, zisk, návrat), body v Domination, bomba (položení, zrychlující pípání, zneškodnění, výbuch), extrakce VIP, oživení, píšťalka a odpočet kola, výhra/prohra kola, posledních 10 s zápasu, úvodní a závěrečná znělka (výhra/prohra/remíza).
- UI: hover/klik tlačítek, přepínače, posuvníky, ready.
- 3D zvuk s útlumem a dolní propustí podle vzdálenosti; AudioMixer Master → SFX / Music / UI napojený na Nastavení → Zvuk (nově přepínač ambientu).

## 2026-10-23 — Testy host + klient a vydání v0.2.1
- Automatické end-to-end testy: dvě (u Medic a Bomb tři) instance hry v Unity batch módu na jednom počítači, jedna hostuje a ostatní se připojí přes LAN (127.0.0.1). Harness ovládá virtuální klávesnici a myš, takže postavy opravdu chodí, skáčou a střílejí, a kontroly se čtou na obou stranách (na klientovi hodnoty klienta). Prošly všechny režimy na obou mapách, boti, hlasování o mapě, pozdní připojení, odchod hosta uprostřed zápasu, časový limit a cesta výsledky → lobby → odveta.
- Opravy z testů (commity `468b08a`, `d17dd75`, `9197ce0`, `b71c9b1`, `81c4b5f`, `8c19f57`):
  - Vzdálení hráči začínali zápas v bodě (0, 0, 0) místo na spawnu (CharacterController zůstal na pozici z prefabu).
  - Respawnutý klient byl na serveru chvíli živý na místě smrti, v CTF tak dostal zpátky upuštěnou vlajku. Server ho teď nechá mrtvého, dokud klient opravdu nedorazí na spawn, a když neodpovídá, pošle mu teleport znovu.
  - Pozdě připojený hráč se spawnul na straně soupeře. Stejná jména dostanou číslo („Jméno 2“).
  - Co-op: číslo vlny a životy nepřátel se synchronizují i klientům, nepřátelé míří z výšky hlavy.
  - Bomb: vzdálený nosič na začátku kola přišel o bombu.
  - Mrtvý hráč mohl lokálně střílet. Přebíjení přerušené smrtí začne po respawnu znovu a výstřel s prázdným zásobníkem sám přebije.
  - Klient po odchodu hosta zůstal v zápase bez kamery, teď se vrátí do menu s hláškou. Host po LEAVE MATCH už neprojde vlastní čekárnou s falešnou chybou.
  - Hra běží i v okně na pozadí. Dřív se zastavila, takže host přepnutím okna zastavil zápas všem.
  - Skok nefungoval při velmi vysokém FPS (`minMoveDistance`).
  - Zvuk: varování o chybějícím nebo dvojitém AudioListeneru při přechodech mezi scénami, 40 varování „Only custom filters can be played“ při startu, odcházející hudba se ztlumila hlasitostí nové skladby.
  - Profil se ukládá atomicky a počítá i kill, který ukončil zápas.
  - Výsledková obrazovka u klienta ukazuje hned konečné skóre, HUD se ukáže až s vlastní postavou.
  - Pódium a tabulka v objektivových režimech řadí hráče podle bodů za cíle, potom podle killů.
- Menu ukazuje správnou verzi (dřív „v0.1.0“).
- Vydání v0.2.1 na itch.io: buildy pro macOS (Universal, `x86_64 arm64`) a Windows (64-bit), lokálně v batch módu a nahrané nástrojem butler. Build v0.2.0 byl pro macOS ve skutečnosti jen pro Apple silicon: architekturu bere Unity z lokálního nastavení *Build Profiles → macOS → Architecture* (výchozí *Apple silicon*), ne z `-buildTarget OSXUniversal`. Postup je teď popsaný v `docs/BUILD.md`.

## 2026-10-23 — Boti do objektivů, airsoft mechaniky, ekonomika a účty (v0.3.0)
- **Boti do všech objektivových režimů** (CTF, Domination, Bomb, VIP Escort, Medic): využit existující hook `ObjectiveMode.CollectExtraParticipants`/`ResolveExtraTransform`; `BotBrain` dostal „objective layer" (nese/brání vlajku, drží body, pokládá/zneškodňuje bombu, eskortuje/honí VIP se sidearmem a pomaleji, oživuje jako medik), respektuje zmrazení v přípravě kola a role. `MatchNet.Bots` je spawnuje i v těchto režimech.
- **Serverově autoritativní munice**: server si vede zásobník/zálohu, odečítá za výstřel, drží reload (`ReloadRpc`/`RefillAmmoRpc`) — modifikovaný klient nevystřílí donekonečna bez přebití.
- **Oprava pádu pod mapu**: respawn na skutečném spawnu místo `(0,2,0)`.
- **Airsoft mechaniky**: lean (Q/E, s kolizí o zeď), výdrž na sprint (lišta v HUDu), **bang rule** (V = eliminace na blízko bez výstřelu, serverově ověřená), **kouřové granáty** (G = hozený fyzický objekt letící obloukem, clona vzniká po dopadu; `SmokeField` blokuje výhled botům).
- **Ekonomika**: Credits za zápas (`PlayerProfile`), výsledková obrazovka ukazuje zisk + zůstatek. **Armory shop** (ARMORY v menu) na nákup zbraní; vlastnictví v profilu, nekoupené zbraně zamčené. Startovní sada: 1911, revolver, Uzi, M870.
- **Účty**: lokální jméno + heslo (`AccountStore`, hashované), přihlášení přepne profil na per-účet soubor; guest má sdílený profil.
- **Testy**: kompilace čistá; editorové checky zelené (objektivy 89/0, medik 39/0, play CTF/DOM/BOMB/VIP/Medic 0 failů). Play checky nově vynucují 0 botů kvůli determinismu.
- Zbývá: gunsmith tuning statů (potřebuje sync natuněných hodnot na server kvůli ověřování hit-reg) a web/UGS účty.

## 2026-11-10 — Režim Infection (zombie airsoft)
- Nový režim `Infection` (`Objectives/Infection.cs`, 11. karta v lobby): **Survivors** (tým 2, jih) proti **Infected** (tým 1, sever).
- Start: všichni jsou přeživší, náhodně se vybere 1 infikovaný na každých započatých 5 hráčů („ALPHA“). Prvních 8 s jsou infikovaní zmrazení na spawnu, přeživší se mezitím rozběhnou.
- Infikovaní nemají repliku: přeživší označují na blízko bang rule (LMB nebo V), jsou o 15 % rychlejší a po zásahu kuličkami se normálně respawnují.
- Přeživší, který padne (označení, pád, cokoli), přejde k infikovaným (`MatchNet.ServerSetTeam`) a respawnuje se na jejich straně. Kdo se připojí do běžícího zápasu (i bot), je infikovaný.
- Výhra: přeživší, když po 4 min stojí aspoň jeden; infikovaní, jakmile nezbude nikdo k nakažení. Režim má vlastní hodiny (časový limit z lobby neplatí).
- HUD: počet infikovaných : přeživších, odpočet „OUTBREAK IN“ / „SURVIVE“, čip s rolí, zelená vinětka pro infikované; poslední přeživší (u 8 hráčů poslední dva) jsou vidět všem jako značka. Ve výsledcích „SURVIVORS WIN“ / „INFECTED WIN“.
- Boti: infikovaný bot běží za nejbližším přeživším a označí ho na 3,5 m; přeživší boti bojují jako v TDM.
- Nové hooky v `ObjectiveMode`: `MeleeOnly` (jen bang, bez zbraně – `WeaponController`, `PlayerAvatar`, `BotBrain`) a `TeamLabel` (vlastní názvy týmů v HUDu).
- Testy: kompilace čistá (Unity 6000.6.8f1, batch), editorové checky beze změny zelené (objektivy 89/0, medik 39/0). Režim zatím **nebyl odehrán** v play módu ani s druhým klientem.

## 2026-11-10 — Zamlžování brýlí
- Nová komponenta `GoggleFog` (jen u vlastního hráče, čistě vizuální): brýle se mlží při sprintu, při zadýchání po běhu (podle chybějící výdrže) a v kouři; při chůzi se pomalu odmlží (vzduch přes větrání).
- Rychlost podle ochrany očí z editoru postavy: Full Face Mask 1,4×, Goggles 1×, Shooting Glasses 0,6×, Mesh Mask 0,3×, bez brýlí se nic nemlží.
- **F** = otřít: setření přes čočku 0,7 s (během něj nejde mířit ani střílet), zůstanou drobné šmouhy; od 30 % zamlžení se ukáže výzva „F WIPE GOGGLES / MASK“. Po respawnu je čočka čistá.
- Mlha je procedurální textura (hustší u okrajů, při silném zamlžení i uprostřed) na vlastním canvasu pod HUDem, takže HUD zůstává čitelný.
- Kouř: klienti si teď pamatují dopadlé mraky i lokálně (`SmokeField.Contains`), server je registroval už dřív kvůli botům.
- Nastavení *Gameplay → Goggle fog* (výchozí zapnuto), klávesa F v seznamu ovládání.
- Testy: kompilace čistá, editorové checky beze změny (89/0, 39/0). Vzhled mlhy a rychlosti zamlžení zatím **nebyly vyzkoušené ve hře**.

## 2026-11-11 — Vítr, váha kuliček, zamlžování jako pravidlo zápasu
- **Vítr** (`Wind.cs`, žádný asset): host v lobby volí OFF / LIGHT / STRONG, server při startu zápasu vylosuje směr a sílu (násobenou faktorem mapy z `MapCatalog`: Town 1, Warehouse 0) a posílá je ve stavu zápasu. `BBBallistics` počítá odpor vzduchu vůči větru, takže kulička se unáší a ověření zásahu na serveru dává stejný výsledek jako u střelce.
- Vítr ve světě: rukávy (windsock) u prvního spawnu obou týmů, v HUDu čip „WIND“ se šipkou otočenou podle kamery.
- **Váha kuliček** 0,20 / 0,25 / 0,28 / 0,32 g: úsťová rychlost klesá s odmocninou hmotnosti, odpor vzduchu s hmotností. Váha jde s výstřelem (`ShootRpc`) a server dráhu přepočítá s ní. 0,28 g (400 CR, rank 2) a 0,32 g (900 CR, rank 6) se kupují v Armory, výchozí je 0,25 g. Těžší boti nosí těžší kuličky.
- **Zamlžování brýlí** je teď pravidlo zápasu z lobby (ON / OFF pro všechny) a mlží se všem stejně rychle bez ohledu na typ brýlí. Osobní přepínač v nastavení zmizel.

## 2026-11-11 — Juggernaut a One in the Chamber
- **Juggernaut** (`Objectives/Juggernaut.cs`): jeden hráč má RPK (dostane ji, i když ji nemá koupenou), pohybuje se na 85 % a nesprintuje. Ostatní se navzájem nezraní a jdou po něm. Každý zásah mu ubere stejný díl zdraví: padne po `HitsFor(hráči)` zásazích (4–12, při 8 hráčích 8), bang počítá za 3. Kdo ho sejme, je nový juggernaut s plným zdravím a nabitou RPK; při pádu nebo odpojení se vybere náhodně. Body = killy v rosteru, vyhrává 15, po čase nejvíc killů. Nad juggernautem je pro všechny značka se zbývajícími zásahy.
- **One in the Chamber** (`Objectives/OneInTheChamber.cs`): jen 1911, každý život začíná s 1 BB a bez zásobníku navíc, jakýkoli zásah zabíjí (bang taky), kill = +1 BB. 3 životy (kdo se připojí později, má 1), bez životů hráč zůstane mrtvý a sleduje. Vyhrává poslední, kdo má životy; po čase nejvíc killů, při shodě víc životů. Životy jsou objektivové body (scoreboard, výsledky).
- Nové hooky v `ObjectiveMode` pro režimy „každý sám“: blokování zranění mezi dvojicí (`BlocksDamageBetween`, používá ho `MatchNet.BlocksDamage`, takže i bang a boti), úprava zranění (`AdjustDamage` v `Health.TakeDamage`), přidělené zbraně (`WeaponGranted`), náboje na začátku života (`LifeAmmo` – owner i server si je nastaví sami, bez zprávy), vítěz-hráč (`DeclarePlayerWinner`). `DecideOnTimeout` u netýmových objektivů nechává rozhodnutí na `MatchNet`.
- `WeaponController`: `ServerGiveAmmo` / `ServerRefillAmmo` (server → owner RPC). Boti mají zásobu nábojů podle režimu a bez nábojů přejdou na bang.
- Lobby: 13 karet režimů, karty jsou nižší (28 px, menší písmo).

## 2026-11-11 — Ocenění po zápase
- `Awards.cs` (`MatchAwards`): server sleduje zásahy, killy, série, bangy, otření brýlí (zpráva `mn_wipe` z `GoggleFog`) a sejmutí juggernauta. Na konci zápasu vybere až 4 ocenění, maximálně 2 na hráče, a pošle je se stavem výsledků (`MatchNet.Awards`).
- Ocenění: **Ghost** (nejvíc killů bez jediného zásahu, aspoň 2), **Rambo** (nejdelší série, aspoň 4), **Bang Master** (aspoň 2 bang eliminace), **Sharpshooter** (nejdelší zásah, aspoň 25 m), **Patient Zero** (Infection, nejvíc nakažených), **Last Survivor** (Infection, jediný přeživší na konci), **Giant Slayer** (Juggernaut, nejvíc sejmutí), **Clear Eyes** (se zapnutým zamlžováním nejvíc killů bez otření, jen hráči). V co-opu se nerozdávají.
- Výsledková obrazovka má pod pódiem karty ocenění (moje zvýrazněné) a drží 8 s místo 6. Každé ocenění dá +25 XP a +40 CR (`PlayerProfile.Commit`, řádky v XP/CR přehledu).
- Testy: kompilace čistá (Unity 6000.6.8f1, batch), editorové checky zelené (objektivy 89/0, medik 39/0). Vítr, váhy kuliček, oba nové režimy ani ocenění zatím **nebyly odehrány** v play módu ani s druhým klientem.

## 2026-11-11 — Mapy Outpost a Forest (Blender), zvuk větru
- **Outpost** (`AirsoftOutpost`, 68 × 47 m) podle PDF návrhu MAP 01: Room 1 (Red, západ) a Room 2 (Blue, východ) 10,5 × 17 m, otevřený MID 28 × 19 m, chodby LINK W/E (6 m), SHORT N/S (3 m, jeden ohyb) a LONG N/S (3,5 m, kolem okraje, ústí do MIDu na straně soupeře). 17 krytů jako v plánu: 10 beden, dvojitá bedna uprostřed, 2 dělicí zdi, 4 barikády z pytlů. Stěny 3,2 m (betonové T-panely), kolem plot, strážní věže, kontejnery a stany (jen vizuál). Středově souměrná.
- **Forest** (`AirsoftForest`, 80 × 80 m jako Town): mírně zvlněný terén, tábory týmů na severní (Red) a jižní (Blue) mýtině (stany, pytle, bedny, vlajka), vyschlý potok napříč s lávkou uprostřed, dva sruby (dveře na obou stranách, okna), ~130 stromů s kolizí kmenů (koruny bez kolize, na minimapě schované), keře jen na krytí pohledu, balvany a padlé kmeny jako kryt, pěšiny. Pás stromů za hranicí jen jako kulisa. Středově souměrná.
- Obě mapy jsou skripty pro Blender (`tools/Blender/`, Blender 5.2, bez MCP, headless): skript postaví geometrii, uloží `.blend` a vyexportuje FBX. Unity je postaví jedním builderem `AirsoftBlenderMapBuilder` podle jmen objektů (`VIS_`, `MMH_`, `GP_` herní body, `Probe_` kontrola os), takže spawny a body režimů žijí v Blenderu u geometrie. Materiály se převedou na URP („Outpost_SandBags“…), jméno určuje zvuk kroků.
- Body pro **všechny režimy** na obou mapách (CTF základny, Domination A/B/C, místa bomby, VIP start/cíl, co-op spawny AI, 4+4 týmové a 8 FFA spawnů), NavMesh, náhledy pro lobby, `MapCatalog` (vítr Outpost 0,8, Forest 0,6), build settings.
- Lobby: hlasování o mapě skládá víc než 2 mapy do mřížky 2 × 2 (nižší náhledy).
- **Zvuk větru** (`WindAmbience`, procedurální, bez assetu): smyčka šumu, hlasitost podle síly větru zápasu, pomalé nárazy (otevírají filtr, takže vítr „zahučí“), pod střechou ztlumený. Vypíná se s ambiencí. Kroky (běh / chůze / plížení podle povrchu) už hra měla.
- Nová kontrola `MapNavCheck` (Tools/Airsoft/Check Map NavMesh): všechny spawny a body režimů na všech mapách leží na NavMeshi a jde se k nim dostat od prvního spawnu.
- Testy: kompilace čistá (Unity 6000.6.8f1, batch), objektivy 89/0, medik 39/0, `MapNavCheck` 254/0 (všechny 4 mapy), zvuková play kontrola 34/0 (vítr hraje), play kontroly režimů na Outpostu i v lese: CTF 16/0, Domination 15/0, Bomb 27/0, VIP 21/0. Mapy zatím **nikdo neodehrál** vlastníma rukama (vzhled, krytí, pocit z pohybu) a zvuk větru nikdo neslyšel.

## 2026-11-11 — Tabulka lídrů, náhodná mapa, lišta zbraní, větší karty režimů
- **Tabulka lídrů** vpravo nahoře v HUDu (`HUDManager.UpdateLeaders`): prvních 5 hráčů v pořadí scoreboardu s počtem killů (Milsim / Medic: zásahy), v týmových režimech barvy týmů, vlastní řádek zvýrazněný a mimo první pětku přidaný dole s pořadím. Kill feed se posouvá pod ni.
- **RANDOM MAP** v hlasování o mapě (`MapCatalog.RANDOM`): dá se pro ni hlasovat i ji zamknout; server při startu vylosuje mapu, která umí zvolený režim, a pokud je víc možností, ne tu naposledy hranou. Režim je v lobby dostupný, když ho umí aspoň jedna mapa (`ObjectiveMode.IsAvailableOn`).
- **Lišta zbraní** ukazuje jen sloty, které hráč může použít (koupené a povolené režimem, např. v One in the Chamber jen 1911), zbytek se skryje a lišta se vycentruje. `WeaponController.SlotOwned` teď zohledňuje i pravidla režimu.
- **Karty režimů** v lobby jsou zase čitelné: 48 px s písmem 18 / 12 v posuvném seznamu (kolečko myši, tenký posuvník), vybraný režim se sám odroluje do zorného pole.
- Testy: kompilace čistá, objektivy 89/0, medik 39/0, zvuková play kontrola 34/0, CTF s náhodnou mapou dvakrát 16/0 (padl Warehouse a Forest). Nový HUD a lobby **nebyly zkontrolované očima** (batch bez grafiky).

## 2026-11-11 — Otírání brýlí rukou, pohyb, průlet dronem na startu, pozadí menu ze hry
- **Otírání brýlí** (`GoggleFog`): mlha je teď pole 160 × 90 hodnot místo jednoho čísla. Klávesa F spustí animaci ruky v rukavici (procedurální sprite: dlaň, prsty, palec, manžeta se suchým zipem), která přejede sklo ve tvaru Z: nahoře zleva doprava, dolů po pravé straně, dole zpátky. Pod dlaní se mlha setře na zbytek (okraje zůstanou mírně zamlžené), k tomu procedurální zvuk otření.
- HUD: panel s municí vpravo dole je bez rámečku a podkladu, u názvu zbraně už není číslo slotu „[1]“.
- **Pohyb** (`FPSController`): vodorovná rychlost má setrvačnost (na zemi rozjezd ~50 m/s², brzda ~60 m/s²). Ve vzduchu se drží rychlost z odrazu, jde jen mírně zatáčet a **skok už nezrychluje** (sprint se ve vzduchu nezapne, rychlost se ořízne na rychlost při odrazu). Po delším skoku krátké zpomalení (60 % na ~0,3 s), takže skákáním se nedá běhat rychleji. Náraz do zdi rychlost sebere.
- **Efekt sprintu**: při sprintu se FOV rozšíří až o 8° (plynule, `FPSController.FovKick`, `WeaponController` ho přičítá k FOV z nastavení).
- **Pohupování kamery** při chůzi: jeden pokles na krok, kývání ze strany na stranu na dva kroky, trochu náklonu; při sprintu rychlejší a výraznější, při plížení poloviční, po dopadu krátké „zhoupnutí“. Řídí se skutečnou rychlostí na zemi. Dá se vypnout v Settings → Gameplay („Camera bob when walking“, pak je i FOV efekt poloviční). Pohupování zbraně se řídí taky skutečným pohybem, ne držením kláves.
- **Začátek zápasu** (`MatchIntro`, `MatchNet.MatchGoTime`): po načtení arény server určí čas startu (7 s průlet + 3 s odpočet) a pošle ho se stavem zápasu. Každý hráč vidí průlet „dronem“: tři pomalé kroužící záběry nad místy, o která v režimu jde (CTF základny, body A/B/C, místa bomby, start a cíl VIP, jinak spawny po celé mapě), prolínačky přes černou, letterbox, popisek „DRONE RECON · MAPA“ s názvem a cílem režimu, bzučení dronu (procedurální zvuk). Pak kamera sletí shora za hráčem do jeho očí a velké 3-2-1 → GO. Do startu se nikdo nehýbe ani nestřílí (`MatchNet.IntroFrozen` v `SpeedFactor`, `WeaponController`, boti stojí, co-op vlny čekají), časový limit běží až od startu, znělka a píšťalka zazní při GO. Bomb / VIP: příprava 1. kola je načasovaná tak, aby skončila právě se startem (jejich 3-2-1 je ten samý). Kdo se načte pozdě, naskočí do průletu tam, kde zrovna je. Batch kontroly průlet přeskakují, s `-intro` ho zapnou.
- **Pozadí menu ze hry** (`MenuShowcase`, `MenuShotsCapture`, `tools/menu_shots.sh`): místo kreslené ilustrace městečka jsou v pravé části menu skutečné snímky ze zápasů s boty na různých mapách, v různých režimech a s různými replikami. Editorový nástroj odehraje zápas (host + 7 botů) a ve chvíli, kdy bot zrovna střílí, vyfotí pohled z první osoby (hráč za spoluhráčem s replikou v ruce), záběr přes rameno bota a kameru za botem (1600 × 900, supersampling 2×). Menu snímky prolíná s pomalým posunem a zoomem, dole ukazuje mapu a režim aktuálního snímku. Na obrazovce připojování běží tytéž snímky hodně ztlumené (v lobby je na přání zase jen tmavé pozadí). Bez snímků zůstane původní ilustrace.
- Kontroly: `ObjectivePlayCheck` čeká na konec průletu (s `-intro` kontroluje zmrazení, let dronu, návrat kamery, start hodin a u Bomb / VIP, že příprava 1. kola běží s odpočtem), s `-introShots` uloží snímky z kamery dronu. `MedicPlayCheck` hostuje na náhodném portu jako ostatní.
- Testy: kompilace čistá (Unity 6000.6.8f1, batch), objektivy 89/0, medik 39/0, zvuková play kontrola 34/0, medik v play módu 27/0, CTF 19/0, Domination 18/0, Bomb 29/0, VIP 23/0; s průletem CTF 22/0, Bomb 33/0, VIP 28/0. Snímky z dronu a vykreslené menu se slideshow zkontrolované očima. **Neověřeno rukama:** pocit z pohybu (setrvačnost, skok, FOV při sprintu, pohupování), ruka při otírání brýlí ve hře, průlet s klientem přes síť.


## 2026-11-11 — Opravy UI, náhodný režim, chat v lobby, ruka při přebíjení, prohlídka zbraně
Vyvíjeno paralelně ve čtyřech větvích (`feat/ui-fixes`, `feat/random-mode`, `feat/lobby-chat`, `feat/viewmodel-anim`) a sloučeno do `herman`.
- **Opravy UI:** zavírací tlačítka v Armory, Profilu a Účtu mají popisek a klávesu ESC vedle sebe (`UIKit.KeyButton`), už se nepřekrývají. 3D náhled postavy v lobby i v editoru ukazuje celou postavu od bot po klobouk (kamera se nastaví podle rozměrů modelu), nápověda „drag to rotate“ je malá v rohu. Hlasování o mapě vysvětluje LOCK (popisek pod nadpisem, odznak „LOCKED BY HOST“, u hostitele UNLOCK). V režimech bez botů se řádky botů skryjí (`NetworkUI.LobbyShowsBots`). Segmentové přepínače, které hráč nemůže měnit, mají vybranou volbu čitelnou.
- **RANDOM MODE** (`MatchNet.RandomMode.cs`, `NetworkUI.RandomMode.cs`): karta s kostkou „?“ první v seznamu režimů, klávesa M ji zařadí do cyklu. Server při STARTu losuje jen z režimů, které mapa umí, vynechá naposledy hraný; s RANDOM MAP losuje platnou dvojici mapa + režim. Sám hráč bez botů dostane Co-op. Zápas, HUD, intro i výsledky ukazují vylosovaný režim, v lobby zůstane vybráno „Random“. Dokud je vybrán, jsou v lobby použitelné časový limit, boti i přihláška za medika; když padne Co-op, boti při startu odejdou.
- **Chat v lobby** (`MatchNet.Chat.cs`, `NetworkUI.Chat.cs`): ikona bubliny vedle LEAVE s počítadlem nepřečtených, panel s historií a polem pro psaní. Zprávy jdou přes server (named messages): server je vyčistí (bez `<` `>`, max 120 znaků), hlídá limit 3 zprávy za 2 s a jméno odesílatele bere ze svého seznamu hráčů. Systémové řádky o příchodu, přejmenování a odchodu; jména v barvě týmu, vlastní oranžové. Historie 60 řádků přežije zápas, smaže se po konci session.
- **Ruka v první osobě** (`ViewmodelHands.cs`): low-poly rukavice (olivová se semišem, manžeta, rukáv k rameni), jen pro vlastního hráče. Přebíjení podle doby přebíjení zbraně: zásobník se vyřízne z modelu (1911, Uzi, P90, AK/RPK, L96), ruka ho vytáhne a upustí, zasune nový a plácne do něj, pak natažení závěru. Brokovnice: tři náboje do okénka a přepumpování; revolver: vyhození nábojnic a rychlonabíječ; M14 a opakovačka: nabíjecí pásek shora. Zrušené přebití vrátí zbraň do původního stavu.
- **Prohlídka zbraně** klávesou **T** (jako v CS:GO, 2,6 s): zbraň se přiblíží, ukáže levý bok, převalí se a ruka ji podepře. Zruší ji střelba, míření, přebíjení, sprint, přepnutí, smrt, menu i otírání brýlí.
- Nové kontroly: `RandomModePlayCheck` (23), `ChatPlayCheck` (35), 15 kontrol losování v `ObjectiveLogicCheck`.
- Testy po sloučení: kompilace čistá, objektivy 104/0, medik 39/0, zvuk 34/0, medik v play módu 27/0, CTF 19/0, Domination 18/0, Bomb 29/0, VIP 23/0, Random mode 23/0, chat 35/0, `MapNavCheck` 254/0.
- **Neověřeno:** druhý skutečný klient přes síť (chat, pole RandomMode ve stavu zápasu, pohled klienta na LOCK), skutečná klávesnice (Enter/Esc v chatu, T), ruka a prohlídka zbraně v interaktivní hře (jen vykreslené snímky ve všech 10 zbraních).

## 2026-11-11 — Vlna v co-opu pod minimapou
- Ukazatel vlny v režimu Co-op („WAVE N“ a počet zbývajících nepřátel) se přesunul ze středu nahoře pod minimapu vlevo nahoře. Je to jen text bez podkladu a rámečku, vycentrovaný pod mapou.
- Testy: kompilace čistá, objektivy 104/0, zvuk 34/0; HUD v co-opu vykreslený a zkontrolovaný očima.

## 2026-11-11 — Hlavy hráčů místo čtverečků, ztlumené automatické testy
- **Portréty hlav** (`HeadPortrait`): u jmen v lobby, ve scoreboardu (týmový i seznam) a na kartě „ELIMINATED BY“ (i v killcamu) je místo barevného čtverečku malá hlava postavy toho hráče nebo bota – tělo, pleť, vlasy, čepice/klobouk a brýle/maska podle jeho vzhledu. Každý vzhled se jednou postaví na skryté scéně hluboko pod mapou, vyfotí vlastní kamerou (pohled ze tří čtvrtin) do textury s průhledným pozadím a zůstane v paměti. Podklad dlaždice je dál barva týmu / trička (ztmavená, aby obličej vynikl); hráč bez zvoleného vzhledu a AI nepřátelé mají jen barvu.
- **Ticho při testech** (`BatchAudioMute`): play kontroly spouštěné z příkazové řádky už nehrají zvuk do reproduktorů (ztlumí se zvuk editoru jako tlačítkem „Mute Audio“, při ukončení se vrátí původní stav). Zvukové zdroje dál hrají, takže zvuková kontrola funguje. Normální editor se nemění.
- Testy: kompilace čistá, objektivy 104/0, zvuk 34/0 (i ztlumený), medik 27/0, Random mode 23/0, chat 35/0, CTF 19/0. Lobby, scoreboard a karta zabití vykreslené a zkontrolované očima (8 hráčů s boty).

## 2026-11-11 — Barva rozhraní na výběr
- Nová záložka **Settings → Interface**: 8 barevných motivů (Blaze – původní oranžová, Green, Blue, Violet, Yellow, Teal, Pink, Red) s náhledem (tlačítko, odznak, úroveň, XP lišta, „(you)“). Volba se ukládá (`GameSettings.UiTheme`, PlayerPrefs „UiTheme“) a RESET TO DEFAULT vrací oranžovou.
- `UIKit.Accent` / `AccentHi` jsou teď vlastnosti podle motivu (+ `AccentLo` pro stisknuté primární tlačítko). Všechno, co se postaví potom, má novou barvu.
- **Přebarvení za běhu** (`ThemeRecolour`): změna se projeví hned i na tom, co už je na obrazovce (menu, lobby, HUD v zápase, nastavení samotné). Prochází všechny UI prvky a vymění staré odstíny akcentu za nové (alfa zůstává), přepíše hex kódy v rich textu a Color pole komponent hry (stavy tlačítek a segmentů), aby se stará barva nevrátila při najetí myší. Barevné vzorky v nastavení a zaměřovač (má vlastní barvu) jsou označené `ThemeFixed` a nemění se. Platforma pod postavou v náhledu se přebarví přes `UIKit.ThemeChanged`.
- Testy: kompilace čistá, objektivy 104/0, zvuk 34/0, medik 27/0, Random mode 23/0, chat 35/0, CTF 19/0, Bomb 29/0. Vykreslené a zkontrolované očima: hlavní menu oranžové → modré za běhu, nastavení se zelenou, lobby ve fialové a zpět v oranžové, HUD v zápase v tyrkysové.

## 2026-11-11 — Barevné díly zbraní
- Modely zbraní měly díly jako pojmenované materiály (Metal, Wood, Magazine, Barrel, Black, Bullet…), ale všechny stejně šedobílé, takže každá zbraň byla jeden bílý kus. `AirsoftGuns.Paint` teď dá každému dílu vlastní barvu a povrch: ocel (lesklejší, trochu kovová), černěná ocel, černý polymer, dřevo (světlé / střední / tmavé), mosazné náboje, červené brokové náboje.
- Podle modelu: 1911 – černý závěr, dřevěné střenky, světlé drobné díly; revolver – ocel, dřevo, mosazné náboje ve válci; Uzi – černá ocel, tmavší rukojeť, šedá pažba; P90 – písková (FDE) s černými díly; M870 – černý polymer, světlejší pouzdro závěru a hlaveň; AK – oranžovohnědé dřevo; RPK – tmavý ořech a světlejší pouzdro (stejný model, jiný vzhled podle jména zbraně); M14 – dřevo a ocel; opakovačka – tmavé dřevo a světlý závěr; L96 – olivová pažba.
- Barvy platí všude, kde se model staví: v ruce (první osoba), u postav hráčů i botů.
- Testy: kompilace čistá, objektivy 104/0, zvuk 34/0, medik 27/0, Random mode 23/0, CTF 19/0. Všech 10 zbraní vykreslených vedle sebe a pistole v ruce ve hře zkontrolované očima.

## 2026-11-11 — Vlastní pravidla zápasu v lobby
- **MATCH RULES** (`MatchNet.Rules.cs`, `NetworkUI.Rules.cs`): tlačítko RULES v dolní liště lobby (odznak CUSTOM, když se pravidla liší od běžné hry) otevře okno s šesti pravidly: HEALTH 50/100/150/200 HP, MOVE SPEED 75–150 %, JUMP normal/high/moon (3× výška, 35 % gravitace), RESPAWN TIME 1/3/6/10 s, LIVES ∞/1/3/5, AMMO normal/infinite. Host je mění (uloží se mu do PlayerPrefs), ostatní je vidí jen pro čtení. Posílají se se stavem zápasu, takže je znají i klienti (rychlost, skok a munici řeší vlastník postavy).
- Kde se uplatní: `Health` (max HP hráčů i botů, prodleva respawnu i odpočet na obrazovce smrti), `FPSController` (rychlost, výška skoku, gravitace), `BotBrain` (rychlost botů), `WeaponController` (nekonečná rezerva u vlastníka i na serveru).
- **Životy**: počítají se ze smrtí v rosteru. Kdo je vyčerpá, zůstane mrtvý (`ObjectiveMode.HoldsRespawn`), obrazovka smrti napíše „Out of lives“. Ve FFA / Gun Game vyhrává poslední hráč, v týmových režimech tým, kterému někdo zůstal (`CheckLivesWin`). Platí jen ve FFA, TDM, Gun Game, CTF a Domination (kolové režimy, One in the Chamber, Infection, Juggernaut, Milsim, Medic a Co-op mají vlastní pravidla – okno to u nich napíše). HUD ukazuje zbývající životy vedle kouře.
- Nová kontrola `RulesPlayCheck` (Tools/Airsoft/Play Check: Match Rules, 21 kontrol): nastavení a uložení pravidel, 200 HP u hráče i botů, respawn 1 s, rychlost / skok / munice, ukazatel životů, vyřazení botů bez návratu a vítězství posledního hráče.
- Testy: kompilace čistá, objektivy 104/0, medik 39/0, zvuk 34/0 (jednou 27/1 – „bot footsteps heard“, bot nedošel dost blízko; dvakrát znovu 34/0), medik v play módu 27/0, Random mode 24/0, chat 35/0, pravidla 21/0, CTF 19/0, Domination 18/0, Bomb 29/0, VIP 23/0. Okno pravidel (host, klient v režimu Bomb) a lobby s odznakem CUSTOM vykreslené a zkontrolované očima. **Neověřeno:** skutečný klient přes síť a pocit z pohybu s jinou rychlostí / měsíčním skokem.

## 2026-11-11 — Přesný počet botů posuvníkem
- V lobby je místo volby OFF / UP TO 4 / UP TO 8 posuvník **BOTS** 0–7 s hodnotou vpravo (OFF, 1 BOT, 5 BOTS…). Host ho táhne, ostatní ho vidí jen pro čtení. `MatchNet.BotsWanted` je teď přesný počet botů (dřív „doplnit do N hráčů“), `HostSetBots` hodnotu ořízne na 0–7 (`BOT_MAX`).
- Boti se vejdou jen do volných míst (lobby má 8): když se připojí hráči, boti uvolní místo, po odchodu hráče se vrátí. Poznámka pod řádkem to napíše („Only 2 bots fit next to 6 players…“).
- Nová volba se ukládá do PlayerPrefs „Airsoft.Bots“; stará „Airsoft.BotFill“ se jednou převede (do 4 hráčů = 3 boti, do 8 = 7).
- Kontroly upravené na nový klíč (Random mode / pravidla se 3 boty, fotky do menu se 7). `RulesPlayCheck` má 3 nové kontroly posuvníku (uložený počet, oříznutí na 7 a doplnění rosteru, návrat na 3) a botům před zásahem vypne ochranu po spawnu – bot, který se objevil později, jinak zásah ignoroval a kontrola občas padala.
- Testy: kompilace čistá, objektivy 104/0, medik 39/0, zvuk 34/0, medik v play módu 27/0, Random mode 23/0, chat 35/0, pravidla 24/0 (4× po sobě), CTF 19/0, Domination 18/0, Bomb 29/0, VIP 23/0. Lobby s 0, 5 a 7 boty vykreslené a zkontrolované očima. **Neověřeno:** tažení posuvníku skutečnou myší a klient přes síť.

## 2026-11-12 — Granát v inventáři
- Kouřový granát (klávesa G) byl v HUDu jen jako malý štítek „SMOKE 2“ nad zdravím, v liště zbraní dole chyběl. Teď má v liště vlastní slot za zbraněmi (s malou mezerou): klávesa **G**, ikonka granátu (tělo, pruh v barvě rozhraní, víčko, páka), nápis SMOKE a počet zbývajících vpravo nahoře.
- Po hodu slot krátce blikne barvou rozhraní; bez granátů zešedne a počet je červeně 0. Po respawnu jsou zase 2. Ukazuje se ve všech režimech, i v Gun Game, kde jsou ostatní sloty skryté.
- Starý štítek „SMOKE“ zmizel, ukazatel životů (pravidla zápasu) je teď na jeho místě nad zdravím.
- Testy: kompilace čistá, objektivy 104/0, medik 39/0, zvuk 34/0, medik v play módu 27/0, Random mode 23/0, chat 35/0, pravidla 24/0, CTF 19/0, Domination 18/0, Bomb 29/0, VIP 23/0. HUD ve FFA vykreslený a zkontrolovaný očima: 2 granáty, bliknutí po hodu, 0 granátů. **Neověřeno:** hod skutečnou klávesou G (stav se v testu nastavil přímo).

## 2026-11-12 — Armory: otáčející se zbraně s cenou a popisem
- Obchod Armory měl jen textový seznam šesti kupovaných zbraní. Teď ukazuje všech 10 replik na kartách 5 × 2: nahoře se model zbraně pomalu otáčí ve 3D, pod ním je název, dvouřádkový popis (co to je a k čemu se hodí), řádek čísel (poškození, kadence, zásobník) a dole cena s tlačítkem BUY. Startovní zbraně mají FREE / STARTER, koupené OWNED, zbraně nad tvůj rank jsou vybledlé a ukazují „RANK n“.
- **`ShopShowcase`**: všechny repliky stojí v mřížce na skryté scéně hluboko pod mapou (vrstva 29), jedna ortografická kamera je natáčí do jedné textury s průhledným pozadím a každá karta ukazuje svůj výřez (`uvRect`). Kamera běží jen při otevřeném obchodu; scéna se postaví při prvním otevření. Každá zbraň se zmenší/zvětší tak, aby se vešla do karty v každém natočení, a má vlastní tři bodová světla (hlavní, modré doplňkové, teplé zezadu).
- Popisy zbraní jsou v `ShopCatalog.Describe`. Okno je širší (1180 px), dolní poznámka se přesunula do nadpisu.
- Oprava: u kuliček se cena psala se symbolem ◆, který úzké písmo nemá (zobrazil se čtvereček) – teď „900 CR“.
- Testy: kompilace čistá, objektivy 104/0, medik 39/0, zvuk 34/0, medik v play módu 27/0, Random mode 23/0, chat 35/0, pravidla 24/0, CTF 19/0, Domination 18/0, Bomb 29/0, VIP 23/0. Otevřený obchod vykreslený dvakrát po sobě (zbraně se točí) a zkontrolovaný očima. **Neověřeno:** nákup kliknutím myší (logika nákupu se neměnila).

## 2026-11-12 — Noční režim
- V lobby je v řádku s větrem a zamlžením brýlí nový přepínač **NIGHT** OFF / ON. Volí ho host (uloží se mu do PlayerPrefs „Airsoft.Night“), ostatní ho vidí jen pro čtení; posílá se s pravidly zápasu (`MatchNet.Night.cs`).
- **`NightLighting`**: když se v noci načte mapa zápasu, každý počítač si ji sám ztmaví (nic se navíc neposílá): slunce na 13 % s modrou barvou měsíce a slabšími stíny, kopie procedurální oblohy s nízkou expozicí a tmavě modrým odstínem (slunce na obloze je teď měsíc), tmavé tříbarevné okolní světlo, slabé odrazy a tmavě modrá mlha, která končí nejpozději v 75 m. Pouliční lampy a lucerny (podle jména objektu, nejvýš 32) dostanou teplé bodové světlo bez stínů: na Town 12, ve Warehouse 4.
- **Čelovka**: místní hráč má na kameře kuželové světlo (62°, dosvit 32 m). Svítí jen jemu, ostatní ji nevidí.
- **Boti v noci**: boti v PvP i nepřátelé v co-opu si všimnou hráče jen na 60 % vzdálenosti (`MatchNet.NightSightFactor`).
- Minimapa se fotí jednou shora: v noci se na ten jeden snímek vrátí denní světlo (`NightLighting.Daylight` / `Night`), aby byla mapa čitelná.
- Testy: kompilace čistá, objektivy 104/0, medik 39/0, zvuk 34/0, medik v play módu 27/0, Random mode 24/0, chat 35/0, pravidla 24/0, CTF 19/0, Domination 18/0, Bomb 29/0, VIP 23/0. Vykresleno a zkontrolováno očima: lobby s NIGHT ON, Town ve dne a v noci (dva pohledy), Warehouse, Outpost a Forest v noci. **Neověřeno:** klient přes síť a hraní noci s myší (zda je čelovka dost silná, zda není moc tma v lese).

## 2026-11-12 — Ruce stále na zbrani
- V pohledu z první osoby byla ruka v rukavici vidět jen při přebíjení a prohlídce, jinak zbraň „visela ve vzduchu“. Teď jsou vidět obě ruce pořád (`ViewmodelHands`): pravá drží rukojeť (prsty kolem rukojeti, ukazováček u spouště), levá u pistole a revolveru podpírá zespodu, u Uzi, P90, brokovnice a pušek drží předpažbí.
- Každá zbraň má v `ViewmodelHands` místo úchopu, sklon rukojeti, držení obouruč (ano/ne) a místo pro levou ruku; rukáv vede od ruky k rameni mimo obraz, takže nikde nekončí ve vzduchu. Pravá ruka je zrcadlová kopie levé.
- Přebití a prohlídka začínají i končí v klidové poloze rukou, takže se ruce na zbraň plynule vrátí (žádné poskočení).
- Oprava: slot brokovnice M870 měl ze starého zástupného modelu „M4A1“ zmenšení na 0,45 a posun, takže brokovnice (a teď i ruce na ní) byla malá. `WeaponController.ApplyWeaponModels` teď po postavení repliky slot vynuluje.
- Testy: kompilace čistá, objektivy 104/0, medik 39/0, zvuk 34/0 (v celé sadě jednou 33/1 – výstřel do země nezahrál dopad kuličky; dvě opakování 34/0, výstřel jde z kamery a ruce nemají kolize, takže to s rukama nesouvisí), medik v play módu 27/0, Random mode 23/0, chat 35/0, pravidla 24/0, CTF 19/0, Domination 18/0, Bomb 29/0, VIP 23/0. Vykresleno a zkontrolováno očima pro všech 10 zbraní (z boku i z první osoby): v klidu, na začátku, uprostřed a na konci přebití a při prohlídce. **Neověřeno:** pohled ve hře s pohybem a střelbou myší (vykreslení bylo ve zkušební scéně), jak ruce vypadají při míření puškohledem.
