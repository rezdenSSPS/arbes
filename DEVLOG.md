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

## 2026-10-21 — Low-poly vzhled, postavy, nové zbraně, nová mapa
- Staženy CC0 low-poly balíčky od Quaternius (postavy, zbraně, budovy, ulice, příroda, rekvizity).
- Lobby: editor postavy (tělo, čepice, barva čepice, tričko, kalhoty, barva kůže, vlasy) s 3D náhledem; vzhled se ukládá a synchronizuje všem hráčům.
- Hráči i nepřátelé mají animované low-poly postavy (idle/chůze/běh/smrt), zbraň v ruce; v TDM tričko podle týmu.
- 10 nových zbraní s low-poly modely (Pistol, Revolver, Ray Gun, Pulse SMG, P90, AK-47, Pulse Rifle, M870, Lightning Gun, AWP).
- Nová mapa "město": křižovatka se silnicemi a chodníky, domy, kontejnerový sklad, park, auta a zátarasy jako krytí, panorama budov, obloha, mlha, post-processing.

## 2026-10-21 — Airsoftové repliky místo sci-fi zbraní
- 10 slotů (klávesy 1–0 = pořadí v Gun Game): 1911 GBB pistole, CO2 revolver, Uzi GBB (plynový samopal), P90 AEG, pružinová brokovnice M870 (3 kuličky na výstřel), AK AEG, RPK podpůrná AEG (300ranový box), M14 DMR (poloautomat), pružinová opakovačka (mířidla), L96 pružinová odstřelovačka.
- Statistiky v airsoftových jednotkách: úsťová rychlost 85–150 m/s, mid-cap/hi-cap zásobníky, semi/auto, vyvážené TTK.
- Nové low-poly modely (Quaternius CC0, převedeno z .blend do FBX): AK47, Uzi, BoltRifle; sci-fi modely odstraněny. RPK používá model AK (delší).

## 2026-10-22 — Zvuk a hudba
- Hra měla nulový zvuk; teď má ~60 zvuků (130 variant) + menu hudbu a dva ambienty. Všechny jsou vlastní, vygenerované procedurálně skriptem `tools/audio/gen_airsoft_audio.py` (syntéza v Pythonu bez vzorků), licence CC0.
- Zbraně: airsoftové výstřely podle typu (AEG „thwack“ s převodovkou, GBB pistole/Uzi, CO2 revolver, pružinová brokovnice s pumpou, pružinová opakovačka a hlasitější L96 se závěrem), přebití (zásobník ven/dovnitř, závěr), cvaknutí naprázdno, výměna zbraně, „tik“ zásahu a potvrzení eliminace.
- Dopady kuliček podle povrchu (beton s odskoky, plech, dřevo, sklo, tráva/pytle, oblečení) a kroky podle povrchu (chůze/sprint/plížení), skok a dopad. Odvozeno z pohybu na každém klientovi, žádné pakety navíc; výstřely ostatních jdou přes existující RPC.
- Milsim/Medic: hráč po zásahu zavolá „HIT!“ (syntetický hlas, každý hráč má svůj).
- Cíle: vlajka (sebrání, zisk, návrat), body v Domination, bomba (položení, zrychlující pípání, zneškodnění, výbuch), extrakce VIP, oživení, píšťalka a odpočet kola, výhra/prohra kola, posledních 10 s zápasu, úvodní a závěrečná znělka (výhra/prohra/remíza).
- UI: hover/klik tlačítek, přepínače, posuvníky, ready.
- 3D zvuk s útlumem a dolní propustí podle vzdálenosti; AudioMixer Master → SFX / Music / UI napojený na Nastavení → Zvuk (nově přepínač ambientu).
