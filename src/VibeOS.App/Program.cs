using VibeOS.App.Windows;

// Temporary smoke test for Task 6 — replaced in Task 9.
var ledger = new SyntheticInputLedger();
var injector = new SendInputInjector(ledger);

Console.WriteLine("VibeOS injection smoke test - tracing a square in 2s. Do not touch the mouse.");
Thread.Sleep(2000);

for (var i = 0; i < 50; i++) { injector.MoveRelative(4, 0); Thread.Sleep(8); }
for (var i = 0; i < 50; i++) { injector.MoveRelative(0, 4); Thread.Sleep(8); }
for (var i = 0; i < 50; i++) { injector.MoveRelative(-4, 0); Thread.Sleep(8); }
for (var i = 0; i < 50; i++) { injector.MoveRelative(0, -4); Thread.Sleep(8); }

Console.WriteLine("Done. Cursor should have traced a square and returned near its origin.");
