# Schema 14 compatibility fixtures

These files were written by the previous implementations, without editing their
serialized headers or payloads:

- `legacy-snow-v14-catalog1.bin`: compiled from main commit
  `48a0adcb79adc793b00a7647fd1caa190a0e8125`. Seed 17, completed six-stage
  employment, winter day 100, and twenty visits to grass tile (20, 30).
- `legacy-jobs-v14-catalog2.bin`: the verified backend Domain assembly from
  build `0.79.0+d9a82f608307`. Seed 17, town namespace 23495, completed employment,
  and one delivered self-addressed letter containing `Legacy café! 🙂`.

Schema 15 must read both layouts and retain snow and jobs together. The fixtures
are disposable generated towns, with no player data.
