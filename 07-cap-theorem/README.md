# Post 7 sample: CAP timeout demo

One key, one partition, three partition behaviors (Refuse, AnswerStale, WaitThenRefuse), chosen per operation in appsettings.json.

Requires the .NET 10 SDK and curl. Verified on SDK 10.0.115 (Ubuntu 24.04).

```bash
cd CapTimeoutDemo
dotnet run --urls http://localhost:5080
# in a second terminal
./demo.sh
```

Compare your output with `CapTimeoutDemo/expected-output.txt`. Timings vary by a few milliseconds, and the attempt count for the `report` call can be three to five.
