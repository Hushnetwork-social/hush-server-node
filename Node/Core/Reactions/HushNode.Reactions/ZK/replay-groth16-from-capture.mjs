import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import process from 'node:process';

function usage() {
  process.stderr.write(
    'Usage: node replay-groth16-from-capture.mjs <verification_key.json> <client-capture.json>\n'
  );
}

async function writeTempJson(prefix, value) {
  const filePath = path.join(
    os.tmpdir(),
    `${prefix}-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}.json`
  );
  await fs.writeFile(filePath, JSON.stringify(value, null, 2));
  return filePath;
}

async function main() {
  const [, , verificationKeyPath, capturePath] = process.argv;

  if (!verificationKeyPath || !capturePath) {
    usage();
    process.exit(2);
  }

  const verifierScriptPath = new URL('./verify-groth16.mjs', import.meta.url);
  const captureRaw = await fs.readFile(capturePath, 'utf8');
  const capture = JSON.parse(captureRaw);
  const payload = capture?.payload ?? capture;

  if (!payload?.proofJson || !Array.isArray(payload?.publicSignals)) {
    throw new Error(
      'Capture file must contain payload.proofJson and payload.publicSignals. Re-run with updated debug capture enabled.'
    );
  }

  const proofPath = await writeTempJson('reaction-proof', payload.proofJson);
  const publicSignalsPath = await writeTempJson('reaction-public-signals', payload.publicSignals);

  try {
    const { spawnSync } = await import('node:child_process');
    const result = spawnSync(
      process.execPath,
      [
        verifierScriptPath.pathname,
        verificationKeyPath,
        proofPath,
        publicSignalsPath
      ],
      {
        encoding: 'utf8'
      }
    );

    if (result.error) {
      throw result.error;
    }

    if (result.stdout) {
      process.stdout.write(result.stdout.trim());
      process.stdout.write('\n');
    }

    if (result.stderr) {
      process.stderr.write(result.stderr);
    }

    process.exit(result.status ?? 0);
  } finally {
    await Promise.allSettled([
      fs.rm(proofPath, { force: true }),
      fs.rm(publicSignalsPath, { force: true })
    ]);
  }
}

await main();
