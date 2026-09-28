// FEAT-012 AC-012-024 and FEAT-016 AC-016-018: recognition is not policy authority.
// This architecture check parses source; comments and type literals are not catalogues.
const fs = require('node:fs');
const path = require('node:path');
const { createRequire } = require('node:module');
const client = path.resolve(process.argv[2]);
const ts = createRequire(path.join(client, 'package.json'))('typescript');
const sourceRoot = path.join(client, 'src');
const fixture = p => /(?:^|\/)(?:fixtures|__fixtures__)(?:\/|\.)/.test(p) || /\.(?:test|spec)\./.test(p);
const planToken = s => /^hushvoting\.(?:direct|veritas|enterprise)(?:\.|$)/.test(s) || /^hushvoting-licence-catalogue\//.test(s);
const policyField = /^(?:eligibleVoterCap|unlimitedElections|termYears|termKind|displayOrder|activationAvailable|allowedGovernanceOptionIds)$/i;
const unwrap = n => {
  while (ts.isAsExpression(n) || ts.isSatisfiesExpression(n) || ts.isParenthesizedExpression(n)) n = n.expression;
  return n;
};
function inspect(file, text) {
  const findings = [];
  const ast = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true, file.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
  const reject = (node, reason) => findings.push(`${file}:${ast.getLineAndCharacterOfPosition(node.getStart(ast)).line + 1}: ${reason}`);
  function visit(n) {
    if (ts.isImportDeclaration(n) || ts.isExportDeclaration(n)) {
      if (n.moduleSpecifier && ts.isStringLiteral(n.moduleSpecifier) && fixture(n.moduleSpecifier.text)) reject(n, 'production imports fixture policy');
    }
    if (ts.isCallExpression(n) && n.arguments.length && ts.isStringLiteral(n.arguments[0]) &&
        (n.expression.kind === ts.SyntaxKind.ImportKeyword || n.expression.getText(ast) === 'require') && fixture(n.arguments[0].text)) reject(n, 'production loads fixture policy');
    if (ts.isPropertyAssignment(n) && policyField.test(n.name.getText(ast).replace(/['"]/g, ''))) {
      const value = unwrap(n.initializer);
      if (ts.isNumericLiteral(value) || ts.isStringLiteral(value) ||
          (ts.isArrayLiteralExpression(value) && value.elements.every(e => !ts.isSpreadElement(e))) ||
          [ts.SyntaxKind.TrueKeyword, ts.SyntaxKind.FalseKeyword].includes(value.kind)) reject(n, 'client defines catalogue policy terms');
    }
    if (ts.isStringLiteral(n) && planToken(n.text)) {
      let p = n.parent;
      // Protocol comparisons and literal types recognize server values without assigning rights.
      if (ts.isLiteralTypeNode(p) || (ts.isBinaryExpression(p) &&
          [ts.SyntaxKind.EqualsEqualsEqualsToken, ts.SyntaxKind.ExclamationEqualsEqualsToken].includes(p.operatorToken.kind))) return;
      let value = n;
      while (p && (ts.isAsExpression(p) || ts.isSatisfiesExpression(p) || ts.isParenthesizedExpression(p))) { value = p; p = p.parent; }
      if (p && ts.isVariableDeclaration(p) && p.initializer === value) return; // wire constant
      if (p && ts.isArrayLiteralExpression(p)) {
        let a = p;
        while (a.parent && (ts.isAsExpression(a.parent) || ts.isSatisfiesExpression(a.parent))) a = a.parent;
        if (ts.isVariableDeclaration(a.parent) && /^KNOWN_.*(?:IDS|VERSIONS)$/.test(a.parent.name.getText(ast)) &&
            p.elements.every(e => ts.isStringLiteral(e) || ts.isIdentifier(e))) return;
      }
      reject(n, 'plan/catalogue identifier used in a client policy definition');
    }
    ts.forEachChild(n, visit);
  }
  visit(ast);
  return findings;
}
// Positive and negative boundary examples guard against weakening the original assertion.
const permitted = "const id = 'hushvoting.veritas.500'; const KNOWN_PLAN_IDS = ['hushvoting.veritas.500']; if (v === 'hushvoting.veritas.500') {} type Id = 'hushvoting.veritas.500';";
if (inspect('boundary.ts', permitted).length) throw new Error('recognition boundary rejected');
for (const prohibited of [
  "const plans = [{ id: 'hushvoting.veritas.500', eligibleVoterCap: 500 }];",
  "const policies = { 'hushvoting.veritas.500': { cap: 500 } };",
  "const known = { eligibleVoterCap: 500, unlimitedElections: true };",
  "import { plan } from './fixtures/plans';",
]) if (!inspect('boundary.ts', prohibited).length) throw new Error('policy duplication boundary not detected');
const findings = [];
let scanned = 0;
function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, e.name), rel = path.relative(sourceRoot, full).split(path.sep).join('/');
    if (e.isDirectory()) { walk(full); continue; }
    if (fixture(rel)) continue;
    if (/\.(?:ts|tsx)$/.test(e.name)) { scanned++; findings.push(...inspect(rel, fs.readFileSync(full, 'utf8'))); }
    if (/\.json$/.test(e.name) && /hushvoting-licence-catalogue\/|hushvoting\.veritas\./.test(fs.readFileSync(full, 'utf8'))) findings.push(`${rel}: bundled catalogue asset`);
  }
}
walk(sourceRoot);
if (!scanned) throw new Error('No client source found');
if (findings.length) { process.stderr.write(findings.join('\n') + '\n'); process.exitCode = 1; }
else console.log(`Client catalogue guard passed: ${scanned} production modules; compatibility permitted, policy definitions rejected.`);
