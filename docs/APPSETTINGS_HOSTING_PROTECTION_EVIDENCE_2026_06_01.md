# Appsettings Hosting Protection Evidence - 2026-06-01

Scope: bang chung cap repo + artifact cho chinh sach bao ve cau hinh khi trien khai hosting. Tai lieu nay khong chua mat khau, API key, cookie, connection string, customer data hoac secret value.

## Policy

- Khong xoa, khong sua, khong mask appsettings.json trong pham vi audit nay.
- Gia tri trong `appsettings.json` duoc giu theo chinh sach hien tai cua chu he thong.
- Bang chung repo chi xac nhan: script/tai lieu/log/package khong in literal secret value ra ngoai `appsettings.json`.
- Bang chung hosting that phai duoc dinh kem dang da redact trong `artifacts/production-evidence/appsettings-hosting/` khi co.

## Required Redacted Artifacts

| Evidence ID | Required proof | Status | Artifact rule |
|---|---|---|---|
| HOST-CONFIG-001 | Hosting file permission cho thu muc ung dung va `appsettings.json`; chi deployment/runtime identity duoc doc. | Pending artifact | `artifacts/production-evidence/appsettings-hosting/HOST-CONFIG-001*` |
| HOST-CONFIG-002 | Config isolation: control panel, file manager, FTP/SFTP/SSH user va deployment user khong chia se tai khoan rong. | Pending artifact | `artifacts/production-evidence/appsettings-hosting/HOST-CONFIG-002*` |
| HOST-BACKUP-001 | Backup hosting/database duoc ma hoa hoac nam trong vung backup duoc nha cung cap bao ve. | Pending artifact | `artifacts/production-evidence/appsettings-hosting/HOST-BACKUP-001*` |
| HOST-BACKUP-002 | Quyen truy cap backup duoc gioi han, co MFA/role hoac quy trinh cap quyen ro rang. | Pending artifact | `artifacts/production-evidence/appsettings-hosting/HOST-BACKUP-002*` |
| HOST-ACCESS-001 | Hosting account co MFA hoac bang chung co che bao ve dang nhap tuong duong. | Pending artifact | `artifacts/production-evidence/appsettings-hosting/HOST-ACCESS-001*` |
| HOST-ACCESS-002 | Danh sach role/user hosting duoc redact, khong co tai khoan du thua hoac public share. | Pending artifact | `artifacts/production-evidence/appsettings-hosting/HOST-ACCESS-002*` |
| HOST-LOG-001 | Runtime log, deployment log, package log va evidence report khong in secret/config value. | Repo guard + pending artifact | `artifacts/production-evidence/appsettings-hosting/HOST-LOG-001*` |

## Repo Guard Evidence

| Guard | Expected behavior | Source |
|---|---|---|
| Production package config hash | Package script ghi hash file config de doi chieu, khong ghi value. | `scripts/Build-ProductionPackage.ps1` |
| Log/package redaction | Package script loai log/local artifact va ghi dong `Config values are not printed by this script.` | `scripts/Build-ProductionPackage.ps1` |
| Test guard | Test doc/script khong chua literal secret/API key/password/connection-string value lay tu `appsettings.json`. | `WMS.Tests/CoreBusinessDeepAuditTests.cs` |

## Sign-Off Rule

- `Pass`: artifact da redact, co ngay, owner, duong dan, va khong hien secret value.
- `Pending artifact`: repo da co khung bang chung, nhung can anh/PDF/log tu hosting thuc te.
- Tai lieu nay khong tu chung nhan hosting that da an toan 100%; no chi khoa chinh sach repo + artifact va danh sach bang chung can nop.
