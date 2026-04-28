# Election Custody on AWS KMS

HushServerNode can run non-dev election custody on Linux only when the
non-Windows envelope providers are configured. AWS KMS is the pragmatic
production provider for the current Lightsail Docker deployment.

## Runtime Configuration

The AWS deployment workflow passes these values into the HushServerNode
container:

```text
Elections__AdminOnlyProtectedTallyEnvelope__Provider=aws-kms
Elections__AdminOnlyProtectedTallyEnvelope__AwsKmsKeyId=<AWS KMS key id, ARN, or alias>
Elections__AdminOnlyProtectedTallyEnvelope__AwsKmsRegion=<AWS region>
Elections__CloseCountingExecutorEnvelope__Provider=aws-kms
Elections__CloseCountingExecutorEnvelope__AwsKmsKeyId=<AWS KMS key id, ARN, or alias>
Elections__CloseCountingExecutorEnvelope__AwsKmsRegion=<AWS region>
AWS_REGION=<AWS region>
```

Required GitHub environment secret:

```text
AWS_KMS_KEY_ID
```

Optional GitHub environment secret:

```text
AWS_CLOSE_COUNTING_KMS_KEY_ID
```

If `AWS_CLOSE_COUNTING_KMS_KEY_ID` is omitted, the CD workflow reuses
`AWS_KMS_KEY_ID`. A separate key is cleaner, but reusing the current KMS key
with separate encryption context is an acceptable Lightsail tradeoff.

Optional GitHub environment variable:

```text
AWS_REGION
```

If `AWS_REGION` is not set, the CD workflow defaults to `eu-central-1`.

## Credentials

Prefer an IAM role/profile attached to the AWS runtime with permissions for the
configured key. The current Lightsail Docker deployment uses a least-privilege
IAM user exposed to the container through environment variables because that is
the practical deploy path for this host.

```json
{
  "Effect": "Allow",
  "Action": [
    "kms:Encrypt",
    "kms:Decrypt",
    "kms:DescribeKey"
  ],
  "Resource": "<kms-key-arn>"
}
```

If the runtime cannot use an IAM role/profile, the CD workflow can pass static
KMS credentials into the container with these GitHub environment secrets:

```text
AWS_KMS_ACCESS_KEY_ID
AWS_KMS_SECRET_ACCESS_KEY
```

Both static credential secrets are required by the current CD workflow because
GitHub performs a real KMS encrypt/decrypt smoke test before replacing the
running server container.

Use KMS key-policy/IAM conditions where possible to restrict access by
encryption context:

```text
kms:EncryptionContext:hush-purpose =
  hush:elections:admin-only-protected-tally-scalar:v1
or
  hush:elections:close-counting-executor-session-key:v1
```

## Local Linux Container Testing

For a local Linux-container test, set the same container environment values
against either:

- a real AWS KMS development key, or
- a LocalStack KMS endpoint using
  `Elections__AdminOnlyProtectedTallyEnvelope__AwsKmsServiceUrl`.

The transparent test provider is still for tests only and must not be used for
real non-dev admin-only elections.
