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
