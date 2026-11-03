# Security policy

## Supported versions

Security fixes are released for the latest minor version only. While Foundation is at `0.x`, upgrade to the newest
release to receive a fix.

## Reporting a vulnerability

Please do not open a public issue for a security problem.

Use GitHub's private reporting: open the repository's **Security** tab and choose **Report a vulnerability**. Include:

- the Foundation and Unity versions and the adapters involved;
- what an attacker can do and under which conditions;
- steps or a small project that reproduce it.

You can expect an acknowledgment within a few days. The report will be assessed, a fix prepared privately where
possible, and the reporter credited in the release notes unless they prefer to stay anonymous.

## Scope

In scope: code in this repository, for example save handling and backup recovery, release and update-link handling,
content loading and the editor content pipeline.

Out of scope: vulnerabilities in third-party libraries that Foundation integrates (UniTask, VContainer, Addressables,
YooAsset, Firebase and others). Report those to their maintainers.
