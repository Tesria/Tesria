# Reporting a security issue

Tesria is self-hosted software; a vulnerability in it is a vulnerability
on every instance somebody runs. Please report privately.

- **Email:** brianintheloopdev@gmail.com with `[tesria security]` in the
  subject. If the repository is public by the time you read this, GitHub's
  private vulnerability reporting on the repository is equally good.
- **Include:** what you found, how to reproduce it, which version
  (`/api/health` reports it), and what you think the impact is. A proof of
  concept against your *own* instance is welcome; against anyone else's is
  not.
- **Expect:** an acknowledgement within a few days, a fix or a mitigation
  as fast as severity warrants, and credit in the changelog unless you
  would rather not.

Please do not open a public issue for something exploitable, and please
give a fix a reasonable head start before writing it up.

What is in scope is described in `docs/security.md`: the threat model,
what each layer defends, and the known gaps. Something on the known-gaps
list is not news, but a way to make it worse than described is.
