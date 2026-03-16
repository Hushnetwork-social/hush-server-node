import fs from 'node:fs/promises';
import path from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';

function logDebug(message) {
  process.stderr.write(`[verify-groth16] ${message}\n`);
}

function writeResultAndExit(payload, exitCode = 0) {
  process.stdout.write(JSON.stringify(payload));
  process.exit(exitCode);
}

async function pathExists(targetPath) {
  try {
    await fs.access(targetPath);
    return true;
  } catch {
    return false;
  }
}

async function resolveSnarkJsSpecifier() {
  const require = createRequire(import.meta.url);
  const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
  const candidateRoots = [];

  const envRoot = process.env.SNARKJS_MODULE_ROOT;
  if (envRoot) {
    candidateRoots.push(envRoot);
  }

  var current = scriptDirectory;
  for (let i = 0; i < 10; i += 1) {
    candidateRoots.push(current);
    candidateRoots.push(path.join(current, 'hush-web-client'));
    const parent = path.dirname(current);
    if (parent === current) {
      break;
    }
    current = parent;
  }

  for (const root of candidateRoots) {
    try {
      const resolved = require.resolve('snarkjs', { paths: [root] });
      return pathToFileURL(resolved).href;
    } catch {
      // Try next root.
    }
  }

  const workspaceHint = path.resolve(scriptDirectory, '..', '..', '..', '..', '..', '..', 'hush-web-client', 'node_modules', 'snarkjs', 'main.js');
  if (await pathExists(workspaceHint)) {
    return pathToFileURL(workspaceHint).href;
  }

  throw new Error(
    `Unable to resolve snarkjs. Checked roots: ${candidateRoots.join(', ')}`
  );
}

async function main() {
  const [, , verificationKeyPath, proofPath, publicSignalsPath] = process.argv;
  logDebug(`argv verificationKeyPath=${verificationKeyPath ?? '<missing>'}`);
  logDebug(`argv proofPath=${proofPath ?? '<missing>'}`);
  logDebug(`argv publicSignalsPath=${publicSignalsPath ?? '<missing>'}`);

  if (!verificationKeyPath || !proofPath || !publicSignalsPath) {
    process.stderr.write('Usage: node verify-groth16.mjs <verification_key.json> <proof.json> <public-signals.json>\n');
    process.exit(2);
  }

  let snarkjs;
  let snarkJsSpecifier;
  try {
    logDebug('Resolving snarkjs');
    snarkJsSpecifier = await resolveSnarkJsSpecifier();
    logDebug(`Resolved snarkjs to ${snarkJsSpecifier}`);
    logDebug('Importing snarkjs');
    snarkjs = await import(snarkJsSpecifier);
    logDebug('Imported snarkjs');
  } catch (error) {
    writeResultAndExit({
      valid: false,
      resolvedSnarkJs: snarkJsSpecifier ?? null,
      error: `Unable to load snarkjs: ${error instanceof Error ? error.message : String(error)}`
    });
  }

  try {
    logDebug('Reading verifier inputs');
    const [vkRaw, proofRaw, publicSignalsRaw] = await Promise.all([
      fs.readFile(verificationKeyPath, 'utf8'),
      fs.readFile(proofPath, 'utf8'),
      fs.readFile(publicSignalsPath, 'utf8')
    ]);
    logDebug(`Read verifier inputs: vkBytes=${vkRaw.length}, proofBytes=${proofRaw.length}, publicSignalsBytes=${publicSignalsRaw.length}`);

    const vk = JSON.parse(vkRaw);
    const proof = JSON.parse(proofRaw);
    const publicSignals = JSON.parse(publicSignalsRaw);
    logDebug(`Parsed verifier inputs: protocol=${vk?.protocol ?? '<null>'}, publicSignalCount=${Array.isArray(publicSignals) ? publicSignals.length : '<not-array>'}`);

    const diagnostics = buildDiagnostics(vk, proof, publicSignals);
    const normalizedCandidates = buildProofCandidates(proof);
    diagnostics.proofCandidateNames = normalizedCandidates.map(candidate => candidate.name);
    logDebug(`Built proof candidates: ${diagnostics.proofCandidateNames.join(', ')}`);

    let lastError = null;
    let lastFalseCandidate = null;
    for (const candidate of normalizedCandidates) {
      try {
        logDebug(`Calling snarkjs.groth16.verify with candidate=${candidate.name}`);
        const valid = await snarkjs.groth16.verify(vk, publicSignals, candidate.proof);
        logDebug(`snarkjs.groth16.verify completed for candidate=${candidate.name} valid=${valid}`);
        if (valid) {
          writeResultAndExit({
            valid: true,
            resolvedSnarkJs: snarkJsSpecifier,
            diagnostics: {
              ...diagnostics,
              selectedCandidate: candidate.name
            }
          });
        }

        lastFalseCandidate = candidate.name;
      } catch (error) {
        lastError = error instanceof Error ? error.message : String(error);
        logDebug(`Candidate ${candidate.name} threw error: ${lastError}`);
        diagnostics.lastCandidateError = {
          name: candidate.name,
          error: lastError
        };
      }
    }

    writeResultAndExit({
      valid: false,
      resolvedSnarkJs: snarkJsSpecifier,
      error: lastError ?? 'snarkjs verification failed for all proof candidates',
      diagnostics: {
        ...diagnostics,
        selectedCandidate: lastFalseCandidate
      }
    });
  } catch (error) {
    logDebug(`Fatal verifier error: ${error instanceof Error ? error.stack ?? error.message : String(error)}`);
    writeResultAndExit({
      valid: false,
      resolvedSnarkJs: snarkJsSpecifier,
      error: error instanceof Error ? error.message : String(error)
    });
  }
}

function buildDiagnostics(vk, proof, publicSignals) {
  return {
    verificationKeyProtocol: vk?.protocol ?? null,
    verificationKeyCurve: vk?.curve ?? null,
    verificationKeyIcLength: Array.isArray(vk?.IC) ? vk.IC.length : null,
    publicSignalCount: Array.isArray(publicSignals) ? publicSignals.length : null,
    proofType: typeof proof,
    proofKeys: proof && typeof proof === 'object' ? Object.keys(proof) : [],
    piAType: Array.isArray(proof?.pi_a) ? 'array' : typeof proof?.pi_a,
    piALength: Array.isArray(proof?.pi_a) ? proof.pi_a.length : null,
    piBType: Array.isArray(proof?.pi_b) ? 'array' : typeof proof?.pi_b,
    piBLength: Array.isArray(proof?.pi_b) ? proof.pi_b.length : null,
    piBInnerLengths: Array.isArray(proof?.pi_b)
      ? proof.pi_b.map(value => Array.isArray(value) ? value.length : null)
      : null,
    piCType: Array.isArray(proof?.pi_c) ? 'array' : typeof proof?.pi_c,
    piCLength: Array.isArray(proof?.pi_c) ? proof.pi_c.length : null,
    proofPreview: proof && typeof proof === 'object'
      ? JSON.stringify(proof).slice(0, 600)
      : String(proof)
  };
}

function buildProofCandidates(proof) {
  const candidates = [];
  const addCandidate = (name, candidateProof) => {
    if (!candidateProof || typeof candidateProof !== 'object') {
      return;
    }

    const serialized = JSON.stringify(candidateProof);
    if (candidates.some(candidate => JSON.stringify(candidate.proof) === serialized)) {
      return;
    }

    candidates.push({ name, proof: candidateProof });
  };

  addCandidate('original', proof);

  if (proof && typeof proof === 'object') {
    const camelToSnake = {
      pi_a: proof.pi_a ?? proof.piA ?? null,
      pi_b: proof.pi_b ?? proof.piB ?? null,
      pi_c: proof.pi_c ?? proof.piC ?? null,
      protocol: proof.protocol ?? proof.Protocol ?? 'groth16',
      curve: proof.curve ?? proof.Curve ?? 'bn128'
    };
    addCandidate('snake_case', camelToSnake);

    if (Array.isArray(camelToSnake.pi_b) && camelToSnake.pi_b.length >= 2) {
      const first = camelToSnake.pi_b[0];
      const second = camelToSnake.pi_b[1];
      const tail = camelToSnake.pi_b.length > 2 ? [camelToSnake.pi_b[2]] : [];

      addCandidate('snake_case_b_swapped', {
        ...camelToSnake,
        pi_b: [
          second,
          first,
          ...tail
        ]
      });

      if (Array.isArray(first) && Array.isArray(second) && first.length >= 2 && second.length >= 2) {
        addCandidate('snake_case_b_inner_swapped', {
          ...camelToSnake,
          pi_b: [
            [first[1], first[0]],
            [second[1], second[0]],
            ...tail
          ]
        });

        addCandidate('snake_case_b_transposed', {
          ...camelToSnake,
          pi_b: [
            [first[0], second[0]],
            [first[1], second[1]],
            ...tail
          ]
        });

        addCandidate('snake_case_b_transposed_inner_swapped', {
          ...camelToSnake,
          pi_b: [
            [second[0], first[0]],
            [second[1], first[1]],
            ...tail
          ]
        });

        addCandidate('snake_case_b_transposed_reversed', {
          ...camelToSnake,
          pi_b: [
            [second[1], first[1]],
            [second[0], first[0]],
            ...tail
          ]
        });

        addCandidate('snake_case_b_rows_and_inner_swapped', {
          ...camelToSnake,
          pi_b: [
            [second[1], second[0]],
            [first[1], first[0]],
            ...tail
          ]
        });
      }
    }
  }

  return candidates;
}

await main();
