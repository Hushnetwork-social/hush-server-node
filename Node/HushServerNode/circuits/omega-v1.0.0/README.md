FEAT-087 approved server circuit drop location.

Required file for a real non-dev verification path:
- `verification_key.json`

This verification key must match the approved client artifact pair:
- `hush-web-client/public/circuits/omega-v1.0.0/reaction.wasm`
- `hush-web-client/public/circuits/omega-v1.0.0/reaction.zkey`

Do not place placeholder files here.

Before running FEAT-087 non-dev benchmarks or browser smoke tests:
1. copy the approved verification key into this directory
2. run the focused server proof-path tests
3. run the readiness/unit gates before any Playwright run

If `verification_key.json` is missing, the correct behavior is fail-closed.
