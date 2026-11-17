# GameCube money audit and acorn menus

AnimalTown calls its currency acorns. The reference is GameCube Animal Crossing's
Bells: its item definitions contain 100, 1,000, 10,000 and 30,000 denominations
(`ITM_MONEY_100`, `ITM_MONEY_1000`, `ITM_MONEY_10000`, `ITM_MONEY_30000`), with a
99,999 wallet limit. See [the original item definitions](https://github.com/ACreTeam/ac-decomp/blob/master/include/m_name_table.h).

## Current implementation

* The demo implements **1,000 and 10,000 acorn bags**. The original game's 100
  and 30,000 denominations are not implemented. No new item IDs are introduced.
* **ID 24, 50,000 acorns**, predates this audit and is not a GameCube denomination.
  It remains readable, droppable, recoverable and redeemable for its full 50,000
  value in older saves. It cannot be newly bought, ordered or buried. Existing
  buried copies still recover through the shovel's existing buried-item flow.
* Currency has a dedicated Money capability and face value, separate from shop
  prices. Money bags have no buy/sell price, are hidden from the catalog, and
  cannot be sold, ordered or purchased through old ownership/stock records.
* Put in wallet credits exactly one selected bag only when its complete amount
  fits within 99,999. Rejection leaves the bag and wallet unchanged. Opening a
  bag works with full pockets and leaves the worn shirt and equipped tool alone.
* Pickup currently puts the bag into a pocket; it does not automatically credit
  the wallet. Drop and basic bury/recover are supported outdoors. Burying does
  not implement the original game's glowing spots or money-tree growth.
* Wallets, savings, loans and bag IDs persist independently for each villager.
  Existing deposit/withdraw/repay operations remain; savings use the demo's
  64-bit balance, not a completed recreation of the original banking system.
  Wallet-to-bag splitting, bag combining and money-tree/money-rock rewards remain
  unimplemented and receive no menu entries.

## Menus

Supported bags show **Put in wallet**, **Drop**, **Bury**, **Quit** outdoors and
**Put in wallet**, **Quit** indoors. The legacy 50,000 bag omits Bury. No money
item offers Equip, Wear, Eat, Plant, Place or Read. Wallet actions retain their
stable action ID even though Put in wallet is now the first row.

The backpack displays the current wallet and each bag's face value. Successful
redemption reports the amount and new balance; overflow explains the wallet
limit. The Sell screen displays Not for sale and no A/Sell action for currency
and other unsellable items instead of a misleading price.

`PocketMenuChecks` covers all bag types and both room contexts, exact-fill and
one-acorn-overflow boundaries, full pockets, repeated redemption, rejected
actions/trade paths, drop/pickup, basic bury, save compatibility and independent
villager balances. `--money-fixture` makes a disposable native test card with
1,000 / 10,000 / legacy 50,000 bags and a 98,999 wallet; Dolphin's
`--resume --money-navigation` checks the four-row popup, exact fill and overflow
screens alongside the existing D-pad/stick/reopen/wraparound checks.
