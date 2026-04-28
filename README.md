# WinInstallerTools

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![Language](https://img.shields.io/badge/language-C%23%20%2F%20.NET-purple)
![Status](https://img.shields.io/badge/status-active-green)

WinInstallerTools is a collection of utilities focused on analyzing, extracting, and understanding Windows installer formats. The project is designed for qa engineers, developers, reverse engineers, security researchers, and power users who need visibility into how installer packages are built and what they contain.

The toolkit begins with support for two common installer technologies:

- **MSIDumper** — tools for Microsoft Installer (`.msi`) packages  
- **NSISDumper** — tools for Nullsoft Scriptable Install System (`.exe`) installers (CODE NOT INCLUDED YET)  

Additional installer formats are planned for future releases.

---

## 🔍 Purpose

Windows installers often contain valuable metadata, embedded files, scripts, registry operations, and custom actions that are not immediately visible to the user.

WinInstallerTools helps by providing utilities for:

- File extraction  
- Installer content inspection  
- Reverse engineering workflows  
- Metadata discovery  
- Script and payload analysis  
- Researching installer behavior  
- Understanding setup package structure  

---

## 📦 Included Tools

## 📁 MSIDumper

Located in:

MSIDumper/

MSIDumper focuses on Microsoft Installer packages (`.msi`).

Planned capabilities include:

- Extract embedded files and CAB contents  
- Read MSI tables  
- View properties and custom actions  
- Inspect features and components  
- Analyze installer structure  

---

## 📁 NSISDumper (CODE NOT INCLUDED YET)

Located in:

NSISDumper/

NSISDumper focuses on NSIS-based executable installers.

Planned capabilities include:

- Extract installer payload files  
- Decode embedded data blocks  
- Inspect NSIS scripts and logic  
- Analyze compression sections  
- Reverse engineer setup behavior  

---

## 🚀 Future Installer Support

WinInstallerTools is intended to grow into a broader toolkit supporting additional Windows installer technologies such as:

- Inno Setup  
- WiX Burn bundles  
- MSIX / AppX  
- InstallShield  
- Self-extracting archives  
- CAB / MSP patch formats  

---

## 🧠 Who This Is For

WinInstallerTools may be useful for:

- QA engineers  
- Software developers  
- Reverse engineers  
- Malware analysts  
- IT administrators  
- Security researchers  
- Digital archivists  
- Curious power users  

---

## 📂 Repository Structure

WinInstallerTools/  
├── MSIDumper/  
├── NSISDumper/  
└── README.md

---

## ⚠️ Disclaimer

This toolkit is intended for legitimate research, debugging, software analysis, interoperability, and educational purposes.

Always respect software licenses, copyrights, and applicable laws when extracting or analyzing third-party installers.

---

## 📌 Status

Early development. Core tooling begins with MSI and NSIS support, with future expansion planned.

---
