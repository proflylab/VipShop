# VipShop: เว็บตัวอย่างซื้อแพ็กเกจ VIP ให้ตัวละคร Trickster ด้วยแต้มเว็บ

**สถานะ: ยังไม่พิสูจน์ในเกม** (ทดสอบแล้ว: unit/web test 53 ข้อด้วยฐานข้อมูลปลอมในหน่วยความจำ ผ่านทุก route จริงของเว็บ; รันเว็บจริงในเครื่องโดยไม่มีฐานข้อมูลและดูหน้าใน Chromium (หน้า login, หน้าแจ้งฐานข้อมูลไม่พร้อม, หน้า admin สร้างแพ็กเกจ, API ตอบ 503/401) และดูภาพทุกหน้าที่ใช้ข้อมูลจากไฟล์ preview ของ test; **ยังไม่เคยต่อฐานข้อมูลจริงบน VM และยังไม่เคยเห็นผลในเกม**)

ผู้เล่น login ด้วยบัญชีเกม เลือกตัวละคร แล้วซื้อแพ็กเกจ VIP ด้วยแต้ม แอดมินกำหนดเองว่าแต่ละแพ็กเกจให้อะไร ฝั่งเกม (GameServer, โมดูล `vip` ของ TrickMod) อ่านแพ็กเกจที่ยังไม่หมดอายุของตัวละครแล้วให้โบนัส

นี่คือ **เว็บตัวอย่าง** ที่ใช้งานได้จริง ถ้าจะเขียนเว็บเอง ให้เรียก procedure ชุดเดียวกัน (หัวข้อ "procedure ที่เว็บเรียก") หรือเรียก JSON API ของโปรแกรมนี้ (หัวข้อ "JSON API")

- โปรแกรมเดียวแบบ MyShop: ASP.NET Core 10 publish เป็นโฟลเดอร์เดียว self-contained ไม่ต้องติดตั้ง .NET / IIS บน VM
- SQL: TrickMod `sql/vip/10_vip.sql` (ตาราง `trickster.dbo.tbl_vip_*` + procedure `gmg_account.dbo.uspw_vip_*`) และ `sql/cashshop/10_cashshop.sql` (แต้ม `tbl_cash`, login `uspw_cash_auth`) ต้องรันทั้งสองไฟล์ก่อน
- รายละเอียดฝั่งเกม: TrickMod `docs/vip`

## กติกา

- **แต้ม = ยอด Cash ของ MyShop** (`gmg_account.dbo.tbl_cash` ต่อบัญชี) ใช้ร่วมกับร้าน MyShop เติมแต้มที่หน้า `/admin` ของ MyShop หรือ `uspw_cash_charge` การซื้อหักแต้มและเขียน `tbl_cash_log` (reason `vip`) ใน transaction เดียวกับการเปลี่ยนแพ็กเกจ
- ตัวละครหนึ่งตัวมีแพ็กเกจที่ใช้งานได้ **ทีละหนึ่ง**
  - ซื้อแพ็กเกจเดิมที่ยังไม่หมด = **ต่อเวลา** จากวันหมดเดิม
  - ซื้อแพ็กเกจอื่นขณะที่อันเดิมยังไม่หมด = **แทนที่ทันที** เวลาที่เหลือของอันเดิมหายไป (หน้ายืนยันเตือนเป็นกรอบสีเหลืองและต้องติ๊ก "I understand ..." ก่อนกดซื้อ)
  - ไม่มี / หมดแล้ว = เริ่มตอนนี้
- วันหมดอายุเป็นวันเวลาแน่นอน (นับต่อแม้ไม่ได้ออนไลน์) เวลาทุกช่องเป็นเวลาเครื่อง SQL Server
- ระยะเวลาเป็นชั่วโมง (`duration_hours` 1..87600) หน้า admin กรอกเป็น วัน + ชั่วโมง หน้าผู้เล่นแสดงเป็น "3 days" / "12 hours" / "1 day 6 hours"
- ค่าที่แพ็กเกจให้: EXP %, TM %, ความเร็ววิ่ง %, อัตราดรอป % (`find_pct` = ไอเทมที่ดรอปจากมอนสเตอร์เท่านั้น ไม่มีผลกับการขุด) (0–100) และ 12 stat (AP AC DX MP MA MD WT DA LK HP DP HV) แต่ละตัวเป็น % (0–100) และบวกตรง (0–10000) หน้าผู้เล่นแสดงเฉพาะค่าที่ไม่ใช่ 0 เช่น `AP +10% +50`
- ราคา 0 ได้ (แพ็กเกจฟรี) ราคาที่เห็นในหน้ายืนยันส่งไปเป็น `@expected_price` ถ้าแอดมินเปลี่ยนราคาระหว่างนั้น การซื้อถูกปฏิเสธ (รหัส 4) ไม่หักเงินผิดราคา
- ในเกม (ตาม VipServer, ยังไม่พิสูจน์ในเกม): `uspw_vip_buy` และ `uspw_vip_package_save` เขียนแถวแจ้งเปลี่ยนลง `trickster.dbo.tbl_vip_change` ตัวละครที่ออนไลน์อยู่จึงได้โบนัสภายในราว 5 วินาที (ทันทีเมื่อเปลี่ยนแมพ/login ใหม่) และมีข้อความในแชทบอกแพ็กเกจ + วันหมดอายุ; แก้ค่าแพ็กเกจภายหลังมีผลกับตัวละครที่ถืออยู่ภายในราว 5 วินาทีเหมือนกัน; ปิดขาย (`enabled = 0`) แค่หยุดขาย คนที่ซื้อไปแล้วใช้ต่อจนหมด ถ้าเขียนเว็บเองแต่ไม่ได้เรียก procedure ชุดนี้ (เช่นแก้ตารางตรง ๆ) จะไม่มีแถวแจ้งเปลี่ยน

## ติดตั้งบน VM

1. เครื่องที่มี .NET 10 SDK: `publish.cmd` → ได้โฟลเดอร์ `publish\` (`VipShop.exe` + `appsettings.json` + `wwwroot\` + `Start.cmd`)
2. คัดลอกโฟลเดอร์ `publish\` ไปไว้บน VM เช่น `server\VipShop`
3. แก้ `appsettings.json` (หัวข้อถัดไป) อย่างน้อย `Admin:Password`, `Vip:Secret` และ `ConnectionStrings:Account`
4. เปิด firewall TCP 24081 (PowerShell แบบ admin):
   `New-NetFirewallRule -DisplayName "Trickster VipShop" -Direction Inbound -Protocol TCP -LocalPort 24081 -Action Allow`
5. ดับเบิลคลิก `Start.cmd` (ปิดหน้าต่าง = หยุดเว็บ) log อยู่ที่ `logs\vipshop-YYYYMMDD.log`
6. เปิด `http://<VM>:24081/admin` สร้างแพ็กเกจ แล้วผู้เล่นเข้า `http://<VM>:24081/`

ต้องรัน SQL ของ TrickMod ก่อน (`Deploy.cmd` ใส่ `cashshop/10_cashshop.sql` และ `vip/10_vip.sql` ใน `tools/dist/oneclick/run_order.txt`) ผู้ใช้ SQL ใน connection string ต้องเรียก procedure `uspw_*` ใน `gmg_account` ได้ (procedure ไปอ่าน/เขียน `trickster` เอง เหมือน MyShop)

**เรื่องความปลอดภัย:** เว็บนี้ฟังแบบ HTTP ธรรมดา รหัสผ่านเกมวิ่งผ่านเครือข่ายแบบไม่เข้ารหัส เหมาะกับ LAN/VM ส่วนตัว ถ้าเปิดสู่อินเทอร์เน็ตให้วางหลัง reverse proxy ที่มี HTTPS (หรือตั้ง Kestrel HTTPS เอง) เว็บไม่เก็บและไม่ log รหัสผ่าน

## appsettings.json

| key | ค่าเริ่มต้น | ความหมาย |
|---|---|---|
| `Urls` | `http://0.0.0.0:24081` | ที่อยู่/พอร์ตที่ฟัง (MyShop ใช้ 24080) |
| `ConnectionStrings:Account` | `Server=127.0.0.1,1433;Database=gmg_account;…;Application Name=VipShop` | SQL Server ของ VM ฐาน `gmg_account` |
| `Vip:SiteName` | `Trickster VIP` | ชื่อบนหัวหน้าเว็บ |
| `Vip:PointName` | `Cash` | ชื่อแต้มบนหน้าเว็บ |
| `Vip:Secret` | ว่าง | กุญแจลงลายมือชื่อ cookie / token / ฟอร์มยืนยัน (ว่าง = สุ่มทุกครั้งที่เปิด ผู้เล่นและ token API หลุด login หลังรีสตาร์ท) ตั้งเป็นข้อความยาวสุ่ม |
| `Vip:SessionHours` | `4` | อายุการ login ของผู้เล่น (cookie และ token API) |
| `Vip:ConfirmMinutes` | `15` | อายุของหน้ายืนยันการซื้อ |
| `Vip:HistoryRows` | `50` | จำนวนแถวในหน้าประวัติของผู้เล่น |
| `Admin:Password` | ว่าง | รหัสหน้า `/admin` และ `/api/admin` (ว่าง = ปิด ตอบ 404) |
| `Admin:SessionHours` | `8` | อายุการ login ของแอดมิน |
| `Admin:LogRows` | `200` | จำนวนแถวในหน้า purchase log |

ค่าทุกตัวส่งทาง command line ได้ด้วย เช่น `VipShop.exe --Urls=http://0.0.0.0:8080`

## หน้าเว็บ

| path | ใคร | ทำอะไร |
|---|---|---|
| GET `/` | ผู้เล่น | ยังไม่ login = หน้า login (id บัญชี + รหัสผ่าน); login แล้ว = แต้ม, ตัวละครของบัญชี + VIP ปัจจุบัน + วันเวลาหมดอายุ + เวลาที่เหลือ, การ์ดแพ็กเกจ (ราคา ระยะเวลา คำอธิบาย โบนัส) พร้อมเลือกตัวละครแล้วกด Buy |
| POST `/login`, `/logout` | ผู้เล่น | เข้า/ออก (login ผิดหน่วง 1 วินาที) |
| GET `/packages` | ทุกคน | รายการแพ็กเกจที่เปิดขาย (ไม่ต้อง login) |
| GET `/buy?char=<char_uid>&package=<package_id>` | ผู้เล่น | หน้ายืนยัน: ตัวละคร แพ็กเกจ โบนัส ราคา แต้มก่อน/หลัง และบอกว่าเป็นการ เริ่มใหม่ / ต่อเวลา / **แทนที่** (เตือน + ต้องติ๊กยอมรับ); แต้มไม่พอ = ไม่มีปุ่มซื้อ |
| POST `/buy` | ผู้เล่น | ซื้อจริง (ฟอร์มมี token ใช้ได้ครั้งเดียว กดซ้ำ/รีโหลดไม่ซื้อซ้ำ) → หน้าผลลัพธ์ |
| GET `/history` | ผู้เล่น | ประวัติการซื้อของบัญชี (ประเภท new / extend / replace, วันหมด, แต้มคงเหลือ) |
| GET `/admin` | แอดมิน | รายการแพ็กเกจทั้งหมด (รวมที่ปิด) แก้ไข / ลบ |
| GET/POST `/admin/package[?id=]` | แอดมิน | ฟอร์มสร้าง/แก้แพ็กเกจ: ชื่อ คำอธิบาย ราคา วัน + ชั่วโมง ลำดับ เปิดขาย, EXP/TM/ความเร็ว/Drop rate %, ตาราง 12 stat × (% / +) ค่าผิดแสดงรายการข้อผิดพลาดและคงค่าที่กรอกไว้ |
| POST `/admin/package/delete` | แอดมิน | ลบ (ถ้ามีคนซื้อแล้ว procedure จะปิดขายแทน) |
| GET `/admin/log` | แอดมิน | การซื้อล่าสุดทุกบัญชี |

- คอลัมน์ Job ในหน้าตัวละครเป็นเลข job ของฐานข้อมูล (ไม่ได้แปลงเป็นชื่ออาชีพ)
- cookie ของผู้เล่นและแอดมินเป็น HttpOnly + SameSite=Strict (เว็บอื่นส่งฟอร์มแทนผู้เล่นไม่ได้) ลงลายมือชื่อ HMAC ไม่มีที่เก็บ session ฝั่ง server
- ฐานข้อมูลต่อไม่ได้ = หน้า "The database is not reachable" (HTTP 503) และ API ตอบ `{"error":"database"}`

## JSON API

ชื่อฟิลด์ทุกตัว = ชื่อคอลัมน์ใน SQL (snake_case) วันเวลาเป็น `"2026-10-12T12:00:00"` (เวลาเครื่อง SQL ไม่มี timezone) ทุก endpoint ที่ต้อง login ส่ง header `Authorization: Bearer <token>` (token จาก `/api/login` หรือ `/api/admin/login` หมดอายุตาม `SessionHours` ออกจากระบบ = ทิ้ง token)

| method + path | body | ตอบ |
|---|---|---|
| GET `/api/packages` | - | `[package]` ที่เปิดขาย เรียง `sort`, `package_id` |
| POST `/api/login` | `{"account": "...", "password": "..."}` | 200 `{"token", "account_code", "account_gid", "expires"}` / 401 `{"error":"login_failed"}` |
| GET `/api/me` | - | `{"account_code", "account_gid", "cash"}` |
| GET `/api/characters` | - | `[{"char_uid", "char_name", "char_level", "job", "package_id", "package_name", "end_date"}]` (3 ช่องท้ายเป็น `null` เมื่อไม่มีแพ็กเกจที่ยังไม่หมด) |
| POST `/api/buy` | `{"char_uid": 5001, "package_id": 2, "expected_price": 10000}` | 200 `{"result":0, "error":"ok", "message", "cash_left", "end_date"}` / 409 รูปเดียวกันเมื่อ `result` 1..5 / 400 body ผิด |
| GET `/api/history?rows=50` | - | `[{"log_id", "log_date", "char_uid", "char_name", "package_id", "package_name", "price", "duration_hours", "kind", "replaced_package", "end_date", "cash_left", ...}]` |
| POST `/api/admin/login` | `{"password": "..."}` | 200 `{"token", "expires"}` / 401 |
| GET `/api/admin/packages` | - | `[package]` ทั้งหมดรวมที่ปิด |
| POST `/api/admin/packages` | `package` (`package_id` 0 หรือไม่ใส่ = สร้างใหม่; ฟิลด์ที่ไม่ใส่ = 0, `enabled` ไม่ใส่ = true) | 200 `{"package_id"}` / 400 `{"error":"bad_request","errors":[...]}` / 404 `{"error":"not_found"}` |
| DELETE `/api/admin/packages/{id}` | - | 200 `{"result":"deleted"}` หรือ `{"result":"disabled"}` (มีประวัติการซื้อ) / 404 |
| GET `/api/admin/log?rows=200` | - | `[log]` ทุกบัญชี (มี `account_code`, `account_gid` เพิ่ม) |

`package` = `{"package_id", "name", "description", "price", "duration_hours", "exp_pct", "tm_pct", "speed_pct", "find_pct", "ap_pct", "ac_pct", "dx_pct", "mp_pct", "ma_pct", "md_pct", "wt_pct", "da_pct", "lk_pct", "hp_pct", "dp_pct", "hv_pct", "ap_add", "ac_add", "dx_add", "mp_add", "ma_add", "md_add", "wt_add", "da_add", "lk_add", "hp_add", "dp_add", "hv_add", "enabled", "sort", "update_date"}`

`find_pct` = อัตราดรอปไอเทมจากมอนสเตอร์ (%) เท่านั้น ไม่มีผลกับการขุด (ชื่อคอลัมน์คงเดิม หน้าเว็บแสดงเป็น "Drop rate")

`result` / `error` ของการซื้อ (= RETURN ของ `uspw_vip_buy`): `0 ok`, `1 not_enough_points`, `2 character_not_owned` (ไม่ใช่ตัวละครของบัญชีหรือถูกลบ), `3 package_unavailable` (ไม่มีหรือปิดขาย), `4 price_changed`, `5 failed` (วันหมดจะเกิน 10 ปี หรือ SQL error ไม่มีอะไรเปลี่ยน) เมื่อไม่สำเร็จ `cash_left` = ยอดปัจจุบัน `end_date` = `null`

ตัวอย่าง (PowerShell):

```powershell
$base = "http://127.0.0.1:24081"
$t = (Invoke-RestMethod -Method Post "$base/api/login" -ContentType application/json -Body '{"account":"tester","password":"12345678"}').token
$h = @{ Authorization = "Bearer $t" }
Invoke-RestMethod "$base/api/characters" -Headers $h
Invoke-RestMethod -Method Post "$base/api/buy" -Headers $h -ContentType application/json -Body '{"char_uid":5001,"package_id":2,"expected_price":10000}'
```

API ไม่เปิด CORS ให้เว็บโดเมนอื่นเรียกจาก browser ตรง ๆ: ให้ server ของเว็บคุณเป็นคนเรียก (หรือเรียก procedure เองตามหัวข้อถัดไป)

## procedure ที่เว็บเรียก

ทั้งหมดอยู่ใน `gmg_account` (ต่อฐานนี้ฐานเดียว) อ่านผลด้วยชื่อคอลัมน์ ตรงกับ `src/VipShop/VipDb.cs`

| procedure | พารามิเตอร์ | ผล |
|---|---|---|
| `uspw_cash_auth` | `@account_gid varchar(50)`, `@login_key varchar(24)`, `@account_code int OUTPUT` | `@account_code` 0 = ไม่ผ่าน |
| `uspw_cash_select` | `@account_code int` | แถว `account_code, account_gid, cash` |
| `uspw_vip_package_list` | `@all bit` (0 = เฉพาะที่เปิดขาย) | แถว `package` (คอลัมน์ตามด้านบน) |
| `uspw_vip_package_save` | `@package_id int OUTPUT` (0 = ใหม่), `@name nvarchar(50)`, `@description nvarchar(200)`, `@price int`, `@duration_hours int`, `@exp_pct`, `@tm_pct`, `@speed_pct`, `@find_pct`, `@ap_pct`..`@hv_pct`, `@ap_add`..`@hv_add` (int ทุกตัว), `@enabled bit`, `@sort int` | RETURN 0 สำเร็จ / 1 ค่าผิด / 2 ไม่พบ; `@package_id` = id |
| `uspw_vip_package_delete` | `@package_id int` | RETURN 0 ลบแล้ว / 1 ปิดขายแทน (มีประวัติซื้อ) / 2 ไม่พบ |
| `uspw_vip_char_list` | `@account_code int` | แถว `char_uid, char_name, char_level, job, package_id, package_name, end_date` |
| `uspw_vip_buy` | `@account_code int`, `@char_uid int`, `@package_id int`, `@expected_price int`, `@cash_left int OUTPUT`, `@end_date datetime OUTPUT` | RETURN 0..5 (ตามตารางรหัสด้านบน) |
| `uspw_vip_history` | `@account_code int`, `@rows int` | แถว `log_id, log_date, char_uid, char_name, package_id, package_name, price, duration_hours, kind, replaced_package, end_date, cash_left` |
| `uspw_vip_log` | `@rows int` | เหมือน history + `account_code, account_gid` |

**login key จากรหัสผ่าน** (ต้องทำแบบนี้ถ้าเขียนเว็บเองแล้วเรียก `uspw_cash_auth`; ตรงกับตัวเปิดเกม TrickModLaunch และ `dbo.fn_Md5_Encrypt` ดู TrickMod `docs/login/README.md`): เอารหัสผ่าน 18 ตัวแรก → ตัดช่องว่างท้าย (RTRIM) → A–Z เป็นตัวเล็ก → MD5 → ไบต์ที่ 4..11 (8 ไบต์) อ่านแบบ big-endian เป็นเลข 64 บิต **มีเครื่องหมาย** → เขียนเป็นเลขฐานสิบ (เช่น `12345678` → ไบต์ `83aa400af464c76d` = `tbl_account.account_pw` → key `-8959278092906084499`) รหัสผ่านต้องเป็น ASCII 32–126 โค้ดอยู่ที่ `src/VipShop/LoginKey.cs`

## สร้างและทดสอบ

```
dotnet build
dotnet test                 # unit test + ทุกหน้า/ทุก API ผ่าน route จริงด้วยฐานข้อมูลปลอม (tests/VipShop.Tests/Support.cs)
publish.cmd                 # -> publish\ (VipShop.exe self-contained win-x64 + appsettings.json + wwwroot + Start.cmd)
```

ดูหน้าตาโดยไม่มีฐานข้อมูล: `set VIPSHOP_PREVIEW=<โฟลเดอร์>` แล้ว `dotnet test` จะเขียนทุกหน้า (`home.html`, `confirm_replace.html`, `admin_edit.html`, ...) พร้อม `assets/site.css` ลงโฟลเดอร์นั้น

รันในเครื่องโดยไม่ต่อฐานจริง (หน้า login / admin ฟอร์มใหม่ เปิดได้ หน้าที่ต้องใช้ฐานตอบ 503):

```
dotnet run --project src/VipShop -- --Urls=http://127.0.0.1:24081 "--ConnectionStrings:Account=Server=127.0.0.1,1;Database=none;User ID=x;Password=x;Connect Timeout=2" --Admin:Password=test
```

โครงสร้าง: `src/VipShop/` (`Program.cs` ตั้งค่า, `PlayerApp.cs` หน้าผู้เล่น, `AdminApp.cs` หน้า admin, `ApiApp.cs` JSON API, `VipDb.cs` เรียก procedure, `Rules.cs` ระยะเวลา/ตรวจค่า/แผนการซื้อ/รหัสผล, `LoginKey.cs`, `Tokens.cs`, `Models.cs`, `Html.cs`, `wwwroot/assets/site.css`), `tests/VipShop.Tests/`
