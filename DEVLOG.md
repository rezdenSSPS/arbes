# Arbes — Vývojový deník (DPM)

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
