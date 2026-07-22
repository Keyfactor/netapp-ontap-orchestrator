## Overview

This NetApp ONTAP Orchestrator extension adds the ability to manage certificates on [NetApp ONTAP](https://www.netapp.com/data-management/ontap-data-management-software/) clusters and Storage VMs (SVMs) through Keyfactor Command, using the ONTAP REST API.

ONTAP maintains a certificate store on each cluster and on each SVM it hosts. This integration lets you inventory those certificates, add and remove them, and discover the scopes (cluster and SVMs) available on a cluster. It exposes two certificate store types that separate the two fundamentally different kinds of certificate ONTAP holds:

- **[ONTAP_CERTS](#ontap_certs)** — end-entity and signer certificates that carry a private key: ONTAP's own `server` certificates (used when ONTAP acts as an SSL server) and `client` certificates (used when ONTAP acts as an SSL client).
- **[ONTAP_TRUSTED](#ontap_trusted)** — trust anchors that are public certificates only: `server_ca` (trusted by ONTAP-as-client to verify external servers) and `client_ca` (trusted by ONTAP-as-server to verify incoming client certificates).

The split reflects a hard constraint of the ONTAP API: the two store types support different operations, and the private key of an existing certificate can never be read back out (see [Certificate types and private keys](#certificate-types-and-private-keys)).

## Requirements

In order to use this integration, you should have:
- An instance of Keyfactor Command v11.0+
- An instance of the Keyfactor Universal Orchestrator v10.4+
- Network access from the Universal Orchestrator to the ONTAP cluster management LIF over HTTPS (443)
- An ONTAP account with sufficient privilege to read and manage certificates (see [Authentication](#authentication) and [Authorization / permissions](#authorization--permissions))
- ONTAP 9.6 or later (the REST API is required; this extension does not use the legacy ONTAPI/ZAPI interface)

### Authentication

This integration authenticates to the ONTAP REST API using HTTP Basic authentication. The credentials are supplied on the certificate store as the Server Username and Server Password, and both fields are PAM-eligible so they can be sourced from a configured Keyfactor PAM provider rather than stored directly.

The **ClientMachine** value on the certificate store is the cluster management hostname or IP address; the extension issues requests against `https://<ClientMachine>/api`.

> :warning: Use a dedicated, least-privilege service account rather than the built-in `admin` account. See [Authorization / permissions](#authorization--permissions).

Lab clusters and the [ONTAP Simulator](https://mysupport.netapp.com) present a self-signed TLS certificate. For those environments, enable the **Ignore SSL Warning** custom field on the certificate store (or the same-named job property on Discovery jobs) to skip TLS validation. Leave it disabled for production clusters that present a trusted certificate.

### Authorization / permissions

The service account used by the integration can only manage certificates in the scopes (cluster and/or SVMs) that its ONTAP role permits. Create an ONTAP role granting access to the certificate REST endpoints and assign it to the service account.

At minimum, the account's role needs access to:
- `GET /api/security/certificates` — inventory
- `POST /api/security/certificates` — add
- `DELETE /api/security/certificates/{uuid}` — remove
- `GET /api/svm/svms` — discovery (enumerate SVM scopes)

For details on creating roles and assigning REST API access in ONTAP, refer to the [ONTAP role-based access control documentation](https://docs.netapp.com/us-en/ontap/authentication/).

### Scope: cluster vs SVM

ONTAP is multi-tenant. A single cluster hosts a cluster (admin) context plus one or more SVMs, and **each maintains its own independent certificate store**. A certificate therefore lives in a specific scope — either cluster scope or a named SVM — and the same certificate name can exist independently in more than one scope.

This integration encodes the scope in the certificate store **Store Path**:
- Enter the **name of an SVM** (for example `vs0`) to manage that SVM's certificates.
- Enter the literal token **`[cluster]`** (including the square brackets) to manage cluster-scoped certificates.

The square brackets are intentional. ONTAP SVM names cannot contain square brackets, so `[cluster]` can never collide with a real SVM name, while still reading clearly as "cluster" in the Command store list.

> :warning: The bare word `cluster` is **not** the cluster token — it is treated as an ordinary SVM name. Only `[cluster]` (with brackets) selects cluster scope. This is deliberate: a cluster may legitimately contain an SVM named `cluster`, and it must remain addressable.

The **Discovery** job enumerates the scopes on a cluster — the cluster token plus one entry per SVM — so you can register the relevant scopes as certificate stores without listing them by hand.

### Certificate types and private keys

Each certificate in ONTAP has a `type`, and the two store types partition those types:

| Store type | ONTAP `type` values | Holds a private key? | Managed as |
| :--------- | :------------------ | :------------------- | :--------- |
| ONTAP_CERTS | `server`, `client` | Yes | End-entity / signer certificate |
| ONTAP_TRUSTED | `server_ca`, `client_ca` | No | Trust anchor (public certificate only) |

A newly provisioned cluster ships with a large set of certificates already present — self-signed `server` certificates for the cluster and each SVM, plus a substantial list of well-known public root CAs (stored as `server_ca`). This is expected; the pre-installed public roots account for most of the count you will see on a first inventory.

> :warning: **The ONTAP REST API never returns a private key on read.** The private key is write-only — supplied when a certificate is installed, and never retrievable afterward. Inventory therefore returns certificate metadata and the public certificate only; it cannot extract private keys from certificates already on the cluster.

The `root_ca` type (a local signing CA that ONTAP generates and uses to sign certificates) is intentionally **not** managed by either store type. A local CA must be generated on the appliance rather than installed, so it does not fit the Add flow; managing local CAs is out of scope for this release.

For store-type-specific configuration and examples, see the documentation for each store type: [ONTAP_CERTS](./ontap_certs.md) and [ONTAP_TRUSTED](./ontap_trusted.md).
