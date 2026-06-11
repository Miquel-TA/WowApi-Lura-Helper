# WoW L'ura Boss Overlay

This is a custom overlay for the L'ura boss fight in World of Warcraft. It tracks and broadcasts symbol callouts in real-time so your group doesn't mess up the mechanics. 

## How it works

The system is split into three straightforward parts:

* **Server (.NET 8):** The API that holds and syncs the current sequence of symbols.
* **Emisor (.NET 4.8.1):** The control panel. You use voice commands or click the buttons to input the symbols (T, Círculo, Triángulo, Equis, Rombo) or clear the list.
* **Receptor (.NET 4.8.1):** The visual overlay. It sits at the top of the screen showing the queued symbols around the boss. Once 5 symbols are locked in, it uses Text-to-Speech to read the exact order out loud up to 3 times.

## Setup

1. Run the **Server**.
2. Open the **Emisor** and **Receptor**.
3. Make sure the URLs in the Emisor and Receptor point to your Server.
4. Toggle the mic or use the buttons on the Emisor to start calling targets.

## Disclaimer

This is a third-party tool made for testing purposes. I am not responsible for any game bans, account suspensions, TOS violations, or wiped raids. Use it entirely at your own risk.
