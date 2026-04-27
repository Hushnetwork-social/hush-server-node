# Admin-Only Protected Tally Custody on AWS KMS

HushServerNode can open admin-only non-dev elections on Linux only when the
admin-only protected tally envelope provider is configured.

## Runtime Configuration

The AWS deployment workflow passes these values into the HushServerNode
container:

```text
Elections__AdminOnlyProtectedTallyEnvelope__Provider=aws-kms
Elections__AdminOnlyProtectedTallyEnvelope__AwsKmsKeyId=<AWS KMS key id, ARN, or alias>
Elections__AdminOnlyProtectedTallyEnvelope__AwsKmsRegion=<AWS region>
AWS_REGION=<AWS region>
```

Required GitHub environment secret:

```text
AWS_KMS_KEY_ID
```

Optional GitHub environment variable:

```text
AWS_REGION
```

If `AWS_REGION` is not set, the CD workflow defaults to `eu-central-1`.

## Credentials

Prefer an IAM role/profile attached to the AWS runtime with permissions for the
configured key:

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

Both static credential secrets must be set together or omitted together.

## Local Linux Container Testing

For a local Linux-container test, set the same container environment values
against either:

- a real AWS KMS development key, or
- a LocalStack KMS endpoint using
  `Elections__AdminOnlyProtectedTallyEnvelope__AwsKmsServiceUrl`.

The transparent test provider is still for tests only and must not be used for
real non-dev admin-only elections.
