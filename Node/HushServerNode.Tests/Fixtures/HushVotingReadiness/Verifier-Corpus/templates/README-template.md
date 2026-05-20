# HushVoting Public Verifier Corpus

Corpus version: `{{CORPUS_VERSION}}`

This repository contains a synthetic public HushVoting verifier corpus. It is designed for local
file replay and does not require HushVoting SaaS access, private repositories, cloud accounts,
databases, or restricted owner/auditor evidence.

## Requirements

- .NET 9 SDK
- Verifier source repository: `https://github.com/Hushnetwork-social/hush-server-node`
- Verifier source ref: `{{VERIFIER_SOURCE_REF}}`
- Verifier project: `Tools/HushVotingVerifier/HushVotingVerifier.csproj`

## Windows PowerShell

```powershell
git clone https://github.com/Hushnetwork-social/hush-server-node hush-server-node
cd hush-server-node
git checkout {{VERIFIER_SOURCE_REF}}
dotnet run --project Tools\HushVotingVerifier\HushVotingVerifier.csproj -- --package {{CORPUS_ROOT}}\packages\sample-good-finalized-election --profile public_anonymous_v1 --output {{CORPUS_ROOT}}\verifier-output\sample-good-finalized-election
```

## Linux Bash

```bash
git clone https://github.com/Hushnetwork-social/hush-server-node hush-server-node
cd hush-server-node
git checkout {{VERIFIER_SOURCE_REF}}
dotnet run --project Tools/HushVotingVerifier/HushVotingVerifier.csproj -- --package "{{CORPUS_ROOT}}/packages/sample-good-finalized-election" --profile public_anonymous_v1 --output "{{CORPUS_ROOT}}/verifier-output/sample-good-finalized-election"
```

## Expected Result

The good sample must return `overallStatus = pass` and exit code `0`.

Every tamper fixture has a matching file under `expected-results/` describing the required primary
result code, required check status, expected overall status, and stable output fields. Secondary
failures may appear only when the documented primary result code remains present.

## Public Boundary

All corpus data is synthetic. The corpus must not contain real voter data, receipt secrets, trustee
raw shares, private witness material, cloud credentials, provider KMS identifiers, raw logs, device
ids, support case joins, or wording that claims legal approval, certification, public-election
approval, real deployment proof, or real customer election proof.
