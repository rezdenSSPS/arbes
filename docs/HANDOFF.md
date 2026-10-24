# Handoff — Airsoft (pro další Claude session)

> Napsáno 2026-10-24. Kontext: stav projektu po vydání **v0.3.0**. Čti spolu s `DEVLOG.md`, `docs/BUILD.md`
> a `docs/GUNSMITH.md`.

## Projekt v kostce
- Unity **6000.6.0f1** (URP), Netcode for GameObjects, listen-server (host = server), macOS dev stroj.
- Herní kód: `Assets/Scripts/FPS/` (namespace `AirsoftFPS`), editor nástroje/checky `Assets/Editor/`.
- Repo: `https://github.com/rezdenSSPS/arbes` (remote `origin`). itch: `rezdenssps/airsoft`.
- **Pracovní adresář:** `/Users/denisreznicek/arbes`.

## Stav gitu
- **`main` = v0.3.0, pushnuto na GitHub.** Feature větev `feature/bots-objectives-airsoft-mechanics` je do main
  už sloučená (merge commit `081c038`). Nová práce: založ si novou větev z `main`.
- Commity v0.3.0 (od `084e101`): serverová munice + pád pod mapu → boti CTF/Dom → boti Bomb/VIP → boti Medic →
  lean/výdrž/bang → kouřáče → fyzický granát → ekonomika MVP → ownership/shop model → shop gating → Armory UI →
  účty → release v0.3.0.

## ✅ Hotovo ve v0.3.0
- **Boti ve všech objektivových režimech** (CTF, Domination, Bomb, VIP Escort, Medic). Využit hook
  `ObjectiveMode.CollectExtraParticipants`/`ResolveExtraTransform`; logika v `BotBrain.cs` (objective layer:
  `ObjectiveMode.BotObjective`/`BotGoal`, task GoTo/Deliver/Plant/Defuse/Revive), per-mód `BotGoal`/`BotWantsUse`
  v `Objectives/*.cs`; spawn v `MatchNet.Bots.cs` (`ModeHasBots` rozšířen). `RoundMode.HealthOf` umí boty.
- **Serverově autoritativní munice** (`WeaponController.cs`): svMag/svReserve, `ReloadRpc`, `RefillAmmoRpc`.
- **Pád pod mapu → respawn na spawnu** (`FPSController.cs`).
- **Lean (Q/E), výdrž na sprint** (`FPSController.cs` + lišta v `HUDManager.cs`).
- **Bang rule (V)** = eliminace na blízko, serverově ověřená (`WeaponController.BangRpc`).
- **Kouřové granáty (G)** = hozený fyzický objekt letící obloukem, clona vzniká po dopadu; `SmokeField.cs`
  blokuje výhled botům (`BotBrain.LineOfSight`). HUD ukazatel.
- **Ekonomika**: Credits za zápas (`PlayerProfile.cs` — `Commit` + `creditLines`), výsledky/menu/profil to ukazují
  (`UI/ProfileUI.cs`). **Armory shop** (`UI/ShopUI.cs`, katalog `ShopCatalog.cs`) — nákup zbraní za Credits;
  vlastnictví v profilu, nevlastněné zbraně zamčené v HUDu + nejdou vybrat (`WeaponController.WeaponUsable`/`SlotOwned`).
- **Lokální účty jméno+heslo** (`AccountStore.cs`, UI `UI/AccountUI.cs`) — přihlášení přepne profil na per-účet soubor.
- **Design canvas** (návrh UI, 8 obrazovek) jako Claude Artifact (odkaz drží uživatel); návrh ekonomiky `docs/GUNSMITH.md`.

## ⏭️ CO JE POTŘEBA UDĚLAT TEĎ

### 1. Dokončit vydání na itch.io (skoro hotové)
GitHub je venku, itch **ne**. Z minulého pokusu vyšel **macOS build jen pro `arm64`, ne Universal**.
- **Oprava (natrvalo):** v `Assets/Settings/Build Profiles/macOS.asset` změň `m_Architecture: 1` → **`2`**
  (0=Intel, 1=ARM64, 2=Universal). Commitni.
- **Editor MUSÍ být zavřený** (batch build/compile jinak hned skončí s návratovým kódem 1 — drží ho zámek projektu;
  ověř `pgrep -f "Unity.app/Contents/MacOS/Unity"`). **Je to CPU-náročné (roztočí ventilátor)** — dělej po dohodě s uživatelem.
- **Build jen mac** (Windows build v `Builds/windows/` je hotový a správný):
  ```bash
  /Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -nographics -projectPath "$PWD" -buildTarget OSXUniversal -buildOSXUniversalPlayer "$PWD/Builds/mac/Airsoft.app" -logFile "$PWD/Logs/build_mac.log"
  ```
  Úspěch: v logu `Build Finished, Result: Success.` a **`lipo -archs Builds/mac/Airsoft.app/Contents/MacOS/Airsoft` → `x86_64 arm64`**.
- **Nahrání** (butler je přihlášený, binárka v `~/butler/butler`):
  ```bash
  ~/butler/butler push Builds/mac rezdenssps/airsoft:mac --userversion 0.3.0
  ~/butler/butler push Builds/windows rezdenssps/airsoft:windows --userversion 0.3.0
  ~/butler/butler status rezdenssps/airsoft
  ```
- Verze je už `bundleVersion: 0.3.0` v `ProjectSettings/ProjectSettings.asset`. (Volitelně tag `v0.3.0` — pozn.: push tagu `v*` spustí GitHub Actions build, který ale selže, protože chybí Unity secrety; buď to neřeš, nebo tag nepushuj.)

### 2. Gunsmith tuning statů (velké, chce playtest)
Návrh v `docs/GUNSMITH.md`. **Pozor:** díly měnící damage / FPS-dostřel / kadenci / zásobník / reload jsou
**serverově ověřované** (`WeaponController.ShootRpc`/`HitRpc`/svMag + token-bucket kadence). Nutné:
- klient pošle serveru per-zbraň natuněné hodnoty (modifikovaný `WeaponData`), server je použije při ověřování,
  jinak odmítne rychlejší střelbu / delší dostřel = **rozbitá hit-reg**;
- gunsmith obrazovka na výběr dílů (ukládat do profilu, vzor `UI/ShopUI.cs`/`ProfileUI.cs`).
Dělat s uživatelem u testu.

## ⚠️ Rizika / na co pozor
- **Změna výchozího chování:** s ownership gatingem hráč **začíná jen se 4 startovními zbraněmi**
  (`1911 GBB Pistol`, `CO2 Revolver`, `Uzi GBB SMG`, `M870 Spring Shotgun`), ostatních 6 se kupuje v ARMORY
  (`ShopCatalog.cs`, mají i rank). Pokud to uživatel nebude chtít takto, přidej přepínač (ekonomika on/off) —
  gating je v `WeaponController.WeaponUsable`/`SlotOwned`.
- **Jen lehce odehráno** ("funguje to"). Boti v objektivech, kouřáče, ekonomika, shop, účty **nemají runtime test**
  přes víc klientů. Plný host+klient+boti harness s virtuální klávesnicí z DEVLOGu 2026-10-23 **NENÍ v repu**.
- **Účty jsou lokální** (soubor na PC, hash hesla); není to síťová bezpečnost — mezikrok k UGS účtům pro web.
- **Ownership gating je client-side** (server zatím vlastnictví neověřuje) — anti-cheat sync je follow-up.

## Jak ověřit (bez hraní, editor zavřený)
- **Kompilace:** `Unity -batchmode -nographics -quit -projectPath "$PWD" -logFile <log>`; hledej `error CS`.
- **Editorové checky přes `-executeMethod`** (všechny byly ve v0.3.0 zelené):
  `AirsoftFPS.Editor.ObjectiveLogicCheck.RunBatch`, `.MedicLogicCheck.RunBatch`,
  `.ObjectivePlayCheck.RunBatch -objMode CTF|DOM|BOMB|VIP`, `.MedicPlayCheck.RunBatch`.
  Play checky samy vynucují 0 botů (determinismus).
- **Ruční playtest** (postup v chatu / README ovládání): host + Fill with bots, projít objektivy, G kouřák,
  V bang, ARMORY nákup, ACCOUNT přihlášení, Credits ve výsledcích.

## Pracovní zásady, co fungovaly
- Commituj po malých vrstvách, po každé fázi batch-kompilace.
- Nikdy nespouštěj batch build/compile, když je otevřený editor (zámek). Ptej se před CPU-náročnými kroky (ventilátor).
- Výstup/release (itch) je nevratný-ish — potvrzuj s uživatelem.
