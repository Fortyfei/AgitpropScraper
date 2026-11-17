# Élesítési útmutató — a nulláról a működő agitprop.cc-ig

Ez a dokumentum **végig azt írja le, amit neked kell megtenned**. A kiindulás: van egy
Cloudflare-re regisztrált `agitprop.cc` domain, és nincs hosting. A végpont: a
`https://agitprop.cc` nyilvánosan elérhető, a növény napi magától frissül, és hetente
menti magát.

Ami **nem** a te dolgod: az alkalmazás buildelése, az image-ek előállítása, a migrációk
futtatása, a konténerek indítási sorrendje, a TLS-tanúsítvány. Ezek automaták.

**Időigény:** kb. 60–90 perc, amiből a legtöbb a VPS telepítése és a letöltések.

**Ahol `<>` van**, azt helyettesíteni kell. A parancsokat a VPS-en futtatod, kivéve ahol
kifejezetten a saját gépedet írom.

---

## 0. Előfeltételek — amire szükséged van, mielőtt elkezded

| Mi | Miért | Hol szerzed be |
|---|---|---|
| `agitprop.cc` a Cloudflare DNS-ében (aktív zóna) | a tunnel erre a domainre publikál | már megvan |
| GitHub-fiók, ami a repóhoz írhat | image-ek GHCR-be, artifact letöltés | már megvan |
| SSH-kulcs a saját gépeden (`~/.ssh/id_ed25519.pub`) | jelszó nélküli belépés a VPS-re | `ssh-keygen -t ed25519` |
| Egy jelszókezelő | 4 titok keletkezik, ezeket nem lehet visszaszerezni | bármi |
| Bankkártya / számla | VPS | — |

> **Miért nincs szükség Cloudflare API tokenre?** A tunnelt a Cloudflare dashboardon
> hozod létre, és csak a **tunnel tokent** kell bemásolni a szerverre. A DNS-rekordot a
> Cloudflare magától létrehozza.

---

## 1. A kód élesítése a `main`-be (a saját gépeden, ~5 perc)

A szerver-telepítés **előtt** a `main`-en benne kell lennie mindannak, ami a
működéshez kell. Jelenleg négy PR vár merge-re; ezeket sorrendben kell beolvasztani:

1. `#91` — OTel függőségek
2. `#92` — Aspire CLI pinnelve a CI-ben
3. `#93` — konténer hardening (`cap_drop`, explicit Production környezet)
4. `#95` — **Watchtower + heti backup** (ez hozza a `stable` taget is)

Mindegyik zöld, egymásra épülnek, a GitHub automatikusan átretargeteli őket.

**Ellenőrzés:** a `main` legfrissebb commitja után az Actions → *Build and Push Images*
lefut, és mind a négy job zöld:

```
test ✓   integration-tests ✓   build-and-push ✓   publish-deployment ✓
```

> Ha a `build-and-push` piros, **ne** folytasd: előbb azt kell megjavítani, különben
> nincs mit letölteni a szerverre.

---

## 2. VPS rendelése (~10 perc + várakozás)

**Mit válassz:** 4 GB RAM, 2 vCPU, 40–80 GB SSD, **Ubuntu 24.04 LTS** (nem 22.04, nem
Debian), lehetőleg EU-s régió. Bármelyik szolgáltató jó (Hetzner CX22, Contabo VPS S,
netcup VPS 1000, Rackforest…). A 2 GB-os gépek **nem** elég: a NER-modellt futtató
consumer 900 MB fölé megy.

Rendeléskor:

- operációs rendszer: **Ubuntu 24.04**, tiszta telepítés;
- **tedd be az SSH publikus kulcsodat** (a legtöbb szolgáltatónál erre külön mező van) —
  ha nincs ilyen mező, a jelszót e-mailben kapod meg;
- IPv4 elég, IPv6 nem kell.

**Ellenőrzés:** kapsz egy IP-címet, és a következő működik a saját gépeden:

```bash
ssh root@<VPS_IP>
```

---

## 3. Az alaprendszer beállítása (a VPS-en, root-ként, ~15 perc)

### 3.1. Frissítés és swap

```bash
apt update && apt upgrade -y
apt install -y curl ca-certificates ufw fail2ban unattended-upgrades

# 2 GB swap: a NER-modell betöltése csúcsot csinál, enélkül az OOM killer ölhet
fallocate -l 2G /swapfile
chmod 600 /swapfile
mkswap /swapfile
swapon /swapfile
echo '/swapfile none swap sw 0 0' >> /etc/fstab

# ellenőrzés: a Swap sornak 2.0G-nek kell lennie
free -h
```

### 3.2. Tűzfal — csak SSH

```bash
ufw default deny incoming
ufw default allow outgoing
ufw allow 22/tcp
ufw --force enable
ufw status          # csak a 22/tcp látszódjon
```

> A web forgalma **nem** jön be a 22-esen kívül: a Cloudflare Tunnel kifelé
> csatlakozik, ezért nincs 80/443 szabály, és a VPS IP-jére nem mutat DNS-rekord.

### 3.3. SSH keményítés

```bash
# kulcs alapú belépés, root jelszó nélkül
sed -i 's/^#\?PermitRootLogin.*/PermitRootLogin prohibit-password/' /etc/ssh/sshd_config
sed -i 's/^#\?PasswordAuthentication.*/PasswordAuthentication no/' /etc/ssh/sshd_config
systemctl restart ssh

# fail2ban
systemctl enable --now fail2ban

# automatikus biztonsági frissítések
dpkg-reconfigure -plow unattended-upgrades     # válaszd az "Yes"-t
```

**Ellenőrzés — még a régi terminál nyitva marad, és egy új ablakban:**

```bash
ssh root@<VPS_IP>        # be kell engednie, jelszó nélkül
ssh -o PreferredAuthentications=password root@<VPS_IP>   # ezt el kell utasítania
```

### 3.4. Docker telepítése

```bash
curl -fsSL https://get.docker.com | sh
docker --version
docker compose version
systemctl enable --now docker
```

---

## 4. A `deploy` felhasználó létrehozása (~5 perc)

Minden üzemeltetési munka ezzel a felhasználóval fog történni, nem root-ként.

```bash
adduser --disabled-password --gecos "" deploy
usermod -aG docker deploy
install -d -o deploy -g deploy /opt/agitprop /opt/agitprop/backups

# ugyanaz az SSH kulcs, ami rootnak már jó
install -d -o deploy -g deploy -m 700 /home/deploy/.ssh
cp /root/.ssh/authorized_keys /home/deploy/.ssh/authorized_keys
chown deploy:deploy /home/deploy/.ssh/authorized_keys
chmod 600 /home/deploy/.ssh/authorized_keys
```

**Ellenőrzés a saját gépedről:**

```bash
ssh deploy@<VPS_IP> 'docker ps && echo DOCKER_OK'
```

Ha a `docker ps` hibát dob, a csoporttagság nem lépett életbe — lépj ki és vissza.

---

## 5. GHCR token és a registry bejelentkezés (~5 perc)

Az image-ek privátak, ezért a szervernek hitelesítve kell tudnia letölteni őket.

### 5.1. Token létrehozása (a saját gépeden)

GitHub → *Settings* → *Developer settings* → *Personal access tokens* →
*Tokens (classic)* → *Generate new token*:

- név: `agitprop-vps-pull`
- lejárat: 90 nap (és jegyezd fel a naptáradba!)
- jogosultság: **csak** `read:packages` (semmi más)

A token csak egyszer látszik — másold a jelszókezelőbe.

### 5.2. Bejelentkezés a VPS-en (`deploy`-ként)

```bash
ssh deploy@<VPS_IP>
echo '<A_TOKEN>' | docker login ghcr.io -u <GITHUB_FELHASZNALONEV> --password-stdin

# a Watchtower ugyanezt a bejelentkezést olvassa egy fix helyről
mkdir -p /opt/agitprop/docker-config
cp ~/.docker/config.json /opt/agitprop/docker-config/config.json
chmod 700 /opt/agitprop/docker-config
chmod 600 /opt/agitprop/docker-config/config.json
```

**Ez a két hely azért kell, mert** a `docker compose pull` a `deploy` saját
konfigurációját használja, a Watchtower konténer viszont a `/opt/agitprop/docker-config`
mappát látja bele a sajátjába. Ha csak az egyiket csinálod meg, vagy a kézi deploy vagy
az automatikus frissítés fog elakadni.

> **Token lejáratkor** mindkét lépést meg kell ismételni. Ez a legvalószínűbb későbbi
> hiba: a tünet az, hogy a merge-ök „nem érnek oda”.

---

## 6. Cloudflare Tunnel létrehozása (~10 perc, böngészőben)

### 6.1. Tunnel

1. Cloudflare dashboard → válaszd az `agitprop.cc` zónát.
2. *Zero Trust* → *Networks* → *Tunnels* → **Create a tunnel**.
3. Típus: **Cloudflared**, név: `agitprop`.
4. A telepítési lépésnél **ne** futtasd a felajánlott parancsot (a konténer fogja
   csinálni) — csak **másold ki a tokent** (a `eyJ...`-vel kezdődő hosszú string).
   Ezt a tokent a 8. lépésben kell majd beírni.

### 6.2. Public hostname

Ugyanabban a tunnelben: *Public Hostname* → **Add a public hostname**:

| Mező | Érték |
|---|---|
| Subdomain | *(üres)* |
| Domain | `agitprop.cc` |
| Path | *(üres)* |
| Type | `HTTP` |
| URL | `web:8080` |

> A `web` a compose-on belüli szolgáltatásnév — **nem** localhost és nem IP. A
> Cloudflare ezt a belső nevet a tunnel másik végén oldja fel.

### 6.3. Ajánlott biztonsági beállítások (ugyanott, 3 perc)

- **SSL/TLS → Overview:** mód = *Full*, és *Edge Certificates* alatt *Always Use HTTPS* = On.
- **Security → WAF → Custom rules:** új szabály
  - név: `block health endpoints`
  - kifejezés: `(http.request.uri.path eq "/health") or (http.request.uri.path eq "/alive")`
  - akció: **Block**
- **Security → WAF → Rate limiting rules:** új szabály a `agitprop.cc`-re,
  10 kérés / 10 másodperc / IP, akció: *Block* — ez váltja ki az app-szintű limitert,
  mert a Blazor egyetlen hosszú WebSocketet tart.
- **DNS:** a tunnel létrehozza a `agitprop.cc` CNAME-et; a `www` átirányítása
  *Rules → Redirect Rules*-szal egy sor.

> **A `/health` blokkolása fontos:** ezek a végpontok éles módban léteznek, de nem
> tartoznak a nyilvánosságra. A konténerek belső healthcheckjeit nem érinti.

---

## 7. Az első telepítés (~10 perc)

### 7.1. A deployment csomag letöltése

1. GitHub → *Actions* → a legutóbbi **Build and Push Images** futás a `main`-en.
2. Alul az *Artifacts* között: **`agitprop-deploy`** → letöltés (zip).

Majd a saját gépedről a VPS-re:

```bash
scp agitprop-deploy.zip deploy@<VPS_IP>:/opt/agitprop/
```

### 7.2. Kicsomagolás és a titkok beírása (a VPS-en, `deploy`-ként)

```bash
cd /opt/agitprop
unzip -o agitprop-deploy.zip
chmod +x deploy/*.sh

# jelszavak generálása — jegyezd fel őket a jelszókezelőbe!
openssl rand -base64 32        # ez lesz POSTGRES_PASSWORD
openssl rand -base64 32        # ez lesz APP_DB_PASSWORD
```

Szerkeszd a `.env`-et (`nano .env`), és töltsd ki:

| Kulcs | Érték |
|---|---|
| `POSTGRES_PASSWORD` | az első generált jelszó (adatbázis-admin, csak te használod) |
| `APP_DB_PASSWORD` | a második generált jelszó (ezzel lép be az alkalmazás) |
| `TUNNEL_TOKEN` | a 6.1-ben kimásolt token |
| `DISCORD_WEBHOOK_URL` | *opcionális* — Discord → csatorna → *Integrations* → *Webhooks* → *Copy URL* |

Ezeket **ne** írd át (a CI töltötte ki, és a bennük lévő útvonalak számítanak):
`CONSUMER_IMAGE`, `RSS_FEED_READER_IMAGE`, `WEB_IMAGE` (mind `:stable`),
`POSTGRES_BINDMOUNT_0`, `WATCHTOWER_BINDMOUNT_0`.

```bash
chmod 600 .env

# ellenőrzés: egyetlen üres érték sem maradhat a kötelező kulcsok között
grep -E '^(POSTGRES_PASSWORD|APP_DB_PASSWORD|TUNNEL_TOKEN|WEB_IMAGE|POSTGRES_BINDMOUNT_0|WATCHTOWER_BINDMOUNT_0)=$' .env \
  && echo 'HIBA: töltsd ki a fenti kulcsokat' || echo 'OK: minden kötelező érték kitöltve'
```

### 7.3. Indítás

```bash
cd /opt/agitprop
docker compose pull
docker compose up -d
```

Ez az első indítás több percig tart, mert a consumer letölti a ~440 MB-os
magyar NER-modellt. **Ez csak egyszer történik meg** — a modell köteten marad.

### 7.4. Az első indítás ellenőrzése

```bash
# a web és a postgres "healthy", a model-ownership "exited (0)"
docker compose ps

# soronként, ahogy kell kinéznie
docker compose logs --tail 20 postgres     # az init script lefutott, nincs ERROR
docker compose logs --tail 20 web          # "Now listening on: http://[::]:8080"
docker compose logs consumer | grep -i "model"   # "... model assets are verified"
docker compose logs --tail 20 cloudflared  # "Registered tunnel connection"
```

**Böngészőben:** `https://agitprop.cc` betölt, a menüben a `/entities` és `/timeline`
oldalak is működnek. (Az első betöltés pár másodperc, amíg a Blazor circuit felépül.)

> Ha 502-t kapsz: a `cloudflared` még nem csatlakozott (várd meg a logban a
> „Registered tunnel connection” sort), vagy a 6.2-es URL nem `web:8080`.

---

## 8. A heti mentés bekapcsolása (~5 perc)

A `backup.sh` a bundle-ben van; a rendszeres futásról egy systemd timer gondoskodik.

```bash
cd /opt/agitprop
sudo cp deploy/systemd/agitprop-backup.* /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now agitprop-backup.timer

# ellenőrzés: látszik a következő futás időpontja
systemctl list-timers agitprop-backup.timer
```

### 8.1. Azonnali próba — ez a fontos lépés

```bash
cd /opt/agitprop
./deploy/backup.sh
ls -lh backups/
```

**A mentés csak akkor ér valamit, ha vissza is tudod állítani.** Egyszer, most, futtasd
le a próbát (a `backups/...` fájlnevet írd be):

```bash
cd /opt/agitprop
set -a; . ./.env; set +a
q() { docker compose exec -T -e PGPASSWORD="$POSTGRES_PASSWORD" postgres psql -U postgres "$@"; }

q -c 'DROP DATABASE IF EXISTS newsfeed_restore'
q -c 'CREATE DATABASE newsfeed_restore'
gunzip -c backups/newsfeed-<DÁTUM>.sql.gz | q -d newsfeed_restore -q -v ON_ERROR_STOP=1
q -d newsfeed_restore -tAc "SELECT 'articles=' || count(*) || ' entities=' || (SELECT count(*) FROM entities) FROM articles"
q -c 'DROP DATABASE newsfeed_restore'
```

Ha az utolsó előtti sor kiír egy darabszámot, a mentés használható.

### 8.2. Havi archívum a GitHubra (opcionális)

Ha szeretnéd, hogy havonta egy példány a VPS-en kívül is legyen:

1. Hozz létre egy **privát** repót (pl. `agitprop-db-archive`).
2. Készíts egy tokent `contents: write` jogosultsággal arra a repóra.
3. Írd a `.env`-be: `ARCHIVE_REPO=<felhasználó>/agitprop-db-archive` és
   `GH_TOKEN=<a token>`.

E nélkül a havi archívum is elkészül, csak a VPS-en marad.

---

## 9. Kész — így működik ezután

| Eset | Mit teszel |
|---|---|
| **Új verzió kiadása** | PR merge a `main`-be. A CI buildel, a Watchtower 5 percen belül kicseréli. **Nem kell SSH-zni.** |
| **Visszaállás** | Revert PR merge a `main`-be — ugyanaz a folyamat, ellenkező irányban. |
| **Vészfék** (ha a merge-ök rosszak lennének) | lásd 10.3. |
| **Havi/heti mentés** | automatikus, vasárnap 03:30-kor. |

### 9.1. Amit érdemes hetente ránézni

```bash
ssh deploy@<VPS_IP>
cd /opt/agitprop
docker compose ps                      # minden "Up"/"healthy"
docker compose logs --tail 50 web      # nincs-e ismétlődő hiba
df -h                                  # a lemez ne teljen be
systemctl list-timers agitprop-backup.timer
```

---

## 10. Hibaelhárítás

### 10.1. A merge után nem történik semmi a site-on

Sorrendben:

```bash
# 1. Lefutott-e a CI és lett-e új "stable" tag?
#    GitHub → Actions: zöld-e a build-and-push?

# 2. Látja-e a Watchtower az új digestet?
docker compose logs --tail 50 watchtower
#    "Found new <image> image" → jó úton vagy
#    "unauthorized" → a GHCR token lejárt vagy hiányzik az 5.2-es másolat

# 3. Kényszerített frissítés:
docker compose pull && docker compose up -d
```

### 10.2. A web "unhealthy", a site nem tölt

```bash
docker compose logs --tail 100 web      # leggyakrabban: DB-kapcsolat vagy migráció
docker compose logs --tail 30 postgres
docker compose ps postgres              # "healthy"?
```

Ha a `web` a migráció közben állt meg, a lemez vagy a DB hibás — a 10.3 szerint állj
vissza az előző buildre, utána nézzük a hibát.

### 10.3. Vészfék: befagyasztás egy konkrét buildre

```bash
cd /opt/agitprop
cp .env .env.stable                                   # elmentjük az automatikus állapotot
sed -i 's|:stable$|:<KORÁBBI_COMMIT_SHA>|' .env        # mindhárom image-et átállítja
docker compose pull && docker compose up -d

# amikor vége a bajnak, visszaadjuk a Watchtowernek:
cp .env.stable .env && docker compose pull && docker compose up -d
```

A commit SHA-t a GitHubon találod (a merge commit azonosítója); pinelt állapotban a
Watchtower nem nyúl a stackhez.

### 10.4. A tunnel nem csatlakozik

```bash
docker compose logs --tail 30 cloudflared
```

- `Invalid tunnel secret` → a `TUNNEL_TOKEN` rossz vagy egy másik tunnelé.
- `context deadline exceeded` → kimenő hálózat/tűzfal blokkol; ellenőrizd:
  `curl -I https://region1.v2.argotunnel.com`.

### 10.5. A consumer újra letölti a modellt

Akkor fordul elő, ha a `agitprop-ner-model` kötet elveszett, vagy a
`model-ownership` konténer nem futott le sikeresen:

```bash
docker compose ps -a model-ownership    # "exited (0)" a helyes érték
docker volume ls | grep ner
```

### 10.6. A `deploy` user nem tud docker parancsot futtatni

A csoporttagság csak új bejelentkezéskor él: lépj ki (`exit`) és beljebb újra.
Ellenőrzés: `id deploy` — szerepelnie kell a `docker` csoportnak.

---

## 11. Amit szándékosan **nem** kell csinálnod

- **Nem** kell .NET SDK vagy Node a VPS-re — minden konténerben fut.
- **Nem** kell kézzel TLS-tanúsítvány — a Cloudflare adja.
- **Nem** kell portot nyitni a webre (80/443) — a tunnel kifelé megy.
- **Nem** kell SQL-t futtatni az induláshoz — a migrációk automatikusak, a séma és a
  `pgmq` sorok az első indításkor jönnek létre.
- **Nem** kell a `docker-compose.yaml`-t szerkeszteni — az a CI-ből jön, és a `.env`
  az egyetlen hely, amit kézzel írsz.

## 12. Amit érdemes felírni magadnak

| Mi | Hol van | Mi történik, ha elveszik |
|---|---|---|
| `POSTGRES_PASSWORD` | `.env` a VPS-en, + jelszókezelő | az adatbázis-admin elérhetetlen (az app nem érinti) |
| `APP_DB_PASSWORD` | ugyanott | az app nem tud kapcsolódni |
| `TUNNEL_TOKEN` | Cloudflare dashboard → a tunnel → *Configure* | új token generálható, a régi érvényteleníthető |
| GHCR token | GitHub → token beállítások | új token + az 5.2 ismétlése |

Ezek közül **egyik sincs a git repóban**, és nem is kerülhet oda.
