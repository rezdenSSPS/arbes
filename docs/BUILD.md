# Automatické buildy a itch.io

Workflow [`.github/workflows/build.yml`](../.github/workflows/build.yml) na GitHubu sestaví hru pro **macOS** a **Windows**
(GameCI `unity-builder`, Unity 6000.6.0f1, Linux runner). Hotové buildy uloží jako artefakty a pokud je
nastavené itch.io, nahraje je tam přes **butler** (kanály `mac` a `windows`). Spolužáci si pak hru stáhnou bez Unity.

Spouští se:
- **ručně** (Actions → Build → Run workflow), volitelně se zaškrtnutím „Nahrát build na itch.io“,
- **automaticky po pushnutí tagu** `v*` (např. `v0.2.0`). Build z tagu se na itch.io nahraje vždy, když je nastavené.

Scény do buildu se berou z *File → Build Profiles → Scene List* (`ProjectSettings/EditorBuildSettings.asset`):
`AirsoftLobby` a `AirsoftArena`. Nová scéna, kterou tam nepřidáš, v buildu nebude.

---

## 1. Unity licence pro CI (jednou)

Secrety se přidávají v repozitáři: **Settings → Secrets and variables → Actions → Secrets → New repository secret**.

### Personal (zdarma) licence – nejspíš tvůj případ

| Secret | Hodnota |
|---|---|
| `UNITY_LICENSE` | celý obsah souboru `Unity_lic.ulf` (otevři v textovém editoru, zkopíruj vše včetně `<?xml …>`) |
| `UNITY_EMAIL` | e-mail Unity účtu |
| `UNITY_PASSWORD` | heslo Unity účtu (GameCI doporučuje heslo bez speciálních znaků) |

Jak získat `Unity_lic.ulf`:
1. Otevři **Unity Hub** a přihlas se účtem, který chceš pro CI použít.
2. **Settings (Preferences) → Licenses → Add → Get a free personal license.**
   Krok *Add* nepřeskakuj, i když už licenci v Hubu vidíš – jinak soubor nemusí vzniknout.
3. Soubor najdeš tady (složky bývají skryté):
   - macOS: `/Library/Application Support/Unity/Unity_lic.ulf` (ve Finderu ⇧⌘G a vlož cestu)
   - Windows: `C:\ProgramData\Unity\Unity_lic.ulf`
   - Linux: `~/.local/share/unity3d/Unity/Unity_lic.ulf`

   Na macOS ho můžeš rovnou zkopírovat do schránky: `pbcopy < "/Library/Application Support/Unity/Unity_lic.ulf"`

Licence aktivovaná na Macu funguje i na Linux runneru GitHubu.

### Plus / Pro licence

Místo `UNITY_LICENSE` přidej `UNITY_SERIAL` (sériové číslo ve tvaru `XX-XXXX-XXXX-XXXX-XXXX-XXXX`
ze stránky Unity Subscriptions) a k tomu `UNITY_EMAIL` a `UNITY_PASSWORD`.

> Pokud secrety chybí, workflow hned v kroku *Check configuration* skončí s českou chybou.

Aktuální postup od GameCI: <https://game.ci/docs/github/activation>

---

## 2. Stránka na itch.io (jednou, volitelné)

1. Přihlas se na <https://itch.io> → **Upload new project**.
2. Vyplň název (např. *Airsoft*). Z adresy stránky se vezme jméno hry: `https://UZIVATEL.itch.io/HRA`.
3. **Kind of project:** *Downloadable*. **Pricing:** *No payments* (nebo dobrovolný příspěvek).
4. Soubory ručně nenahrávej – postará se o to butler.
5. **Visibility:** *Draft* na zkoušku. Spolužákům pak buď *Restricted* (jen s odkazem / heslem) nebo *Public*.
6. Ulož (*Save & view page*).

API klíč pro butler:
- itch.io → **Settings → API keys → Generate new API key**, zkopíruj ho.
- V GitHubu přidej secret **`BUTLER_API_KEY`** s tímto klíčem.

Jméno účtu a hry se nastavuje jako **proměnné**, ne secrety:
**Settings → Secrets and variables → Actions → Variables → New repository variable**

| Proměnná | Příklad | Význam |
|---|---|---|
| `ITCH_USER` | `rezden` | uživatelské jméno na itch.io (část před `.itch.io`) |
| `ITCH_GAME` | `airsoft` | jméno hry z adresy stránky (část za lomítkem) |

Dokud chybí `BUTLER_API_KEY`, `ITCH_USER` nebo `ITCH_GAME`, job *Publish to itch.io* se přeskočí
a buildy jsou jen v artefaktech.

Po prvním nahrání se na stránce hry (Edit game) u souborů ukážou kanály `mac` a `windows` s ikonkou platformy.
Zkontroluj, že u Windows je zaškrtnuté *Windows* a u Macu *macOS*.

---

## 3. Spuštění buildu

1. GitHub → záložka **Actions** → vlevo **Build** → **Run workflow**.
2. Vyber větev (`main`) a případně zaškrtni *Nahrát build na itch.io*.
3. Build trvá napoprvé cca 30–60 min (stahuje se Unity image a importuje projekt), další běhy jsou rychlejší
   díky cache složky `Library`.
4. Hotové buildy najdeš dole na stránce běhu v sekci **Artifacts** (drží se 14 dní):
   - `Airsoft-windows` – rozbal zip a spusť `Airsoft.exe`,
   - `Airsoft-mac` – v zipu je `Airsoft-mac.tar.gz` (kvůli zachování práv), rozbal ho dvojklikem → `Airsoft.app`.

---

## 4. Vydání verze tagem

```bash
git checkout main
git pull
git tag v0.2.0
git push origin v0.2.0
```

Push tagu spustí build automaticky a (je-li nastavené itch.io) nahraje ho s verzí `0.2.0`.
Verzi počítá GameCI (*Semantic*) z posledního tagu; mezi tagy má tvar `0.2.<počet commitů>`.
Tag piš vždy ve tvaru `vMAJOR.MINOR.PATCH`.

---

## 5. Pro hráče (pošli spolužákům)

- **Nejjednodušší je aplikace itch** (<https://itch.io/app>): hru nainstaluje, aktualizuje a na Macu nehlásí „poškozená aplikace“.
- **Windows:** SmartScreen může hlásit neznámého vydavatele → *Další informace → Přesto spustit*.
- **macOS:** build není podepsaný Apple certifikátem. Když macOS hlásí, že aplikace je poškozená nebo ji nelze otevřít,
  přesuň `Airsoft.app` do Aplikací a v Terminálu spusť:
  ```bash
  xattr -cr /Applications/Airsoft.app
  ```
  případně *Nastavení systému → Soukromí a zabezpečení → Přesto otevřít*.
- Online hra (Relay) potřebuje internet. Hráči nic nastavovat nemusí, Unity Gaming Services projekt je v repu.

---

## Řešení problémů

| Problém | Co dělat |
|---|---|
| `Chybí secret UNITY_LICENSE…` | přidej secrety podle kroku 1 |
| Aktivace licence selže | vygeneruj `Unity_lic.ulf` znovu (krok 1.2 s *Add*) a přepiš secret; zkontroluj e-mail a heslo |
| Build spadne na místě na disku | workflow už maže nepotřebné SDK; případně smaž staré cache v *Actions → Caches* |
| `Insufficient shared memory` | řeší `unity-builder@v6` sám; pokud by se vrátilo, zkontroluj novou verzi GameCI |
| Nic se nenahrálo na itch.io | v běhu je u *Check configuration* poznámka, co chybí; ruční běh musí mít zaškrtnuté *Nahrát* |
| `butler push` hlásí chybu oprávnění | `ITCH_USER` / `ITCH_GAME` musí přesně odpovídat adrese stránky a klíč musí patřit stejnému účtu |

Dokumentace: GameCI builder <https://game.ci/docs/github/builder>, butler <https://itch.io/docs/butler/>.
