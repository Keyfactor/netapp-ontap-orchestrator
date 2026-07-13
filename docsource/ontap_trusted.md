## Overview

The ONTAP_TRUSTED certificate store type manages **trust anchors** on an ONTAP cluster or SVM — public CA certificates that ONTAP uses to verify other parties, with no private key. These are the `server_ca` certificates (trusted by ONTAP acting as an SSL client to verify an external server) and `client_ca` certificates (trusted by ONTAP acting as an SSL server to verify an incoming client certificate).

For end-entity certificates that carry a private key (ONTAP's own `server`/`client` certificates), use the [ONTAP_CERTS](./ontap_certs.md) store type instead.

## Requirements

1. [Certificate Format](#certificate-format)
1. [Scope and the Store Path](#scope-and-the-store-path)
1. [Configuring the Certificate Store](#configuring-the-certificate-store)
1. [Supported Operations](#supported-operations)
1. [Managing pre-installed roots](#managing-pre-installed-roots)
1. [Example](#example)
1. [Troubleshooting](#troubleshooting)

---

#### Certificate Format

Certificates in this store are ONTAP certificates of type `server_ca` or `client_ca`. Each is a public certificate only — **no private key is installed**, and the store type forbids private key material.

The `CertType` entry parameter selects which ONTAP type to install the entry as:

| CertType | Meaning |
| :------- | :------ |
| `server_ca` | Trusted by ONTAP acting as an SSL **client**, to verify an external **server**'s certificate. |
| `client_ca` | Trusted by ONTAP acting as an SSL **server**, to verify an incoming **client** certificate. |

Choose the value that matches the trust direction you need; the two are not interchangeable.

---

#### Scope and the Store Path

The **Store Path** identifies the scope whose trust anchors the store manages:
- an **SVM name** (for example `vs0`) to manage that SVM's trust anchors, or
- the literal token **`[cluster]`** (square brackets included) to manage cluster-scoped trust anchors.

The **ClientMachine** is the cluster management hostname or IP; requests are issued against `https://<ClientMachine>/api`. See the [Overview](./content.md#scope-cluster-vs-svm) for a full explanation of scope.

---

#### Configuring the Certificate Store

| Field | Description |
| :---- | :---------- |
| ClientMachine | The ONTAP cluster management hostname or IP address. |
| StorePath | The scope: an SVM name, or `[cluster]` for cluster scope. |
| Server Username | ONTAP service account username (PAM-eligible). |
| Server Password | ONTAP service account password (PAM-eligible). |
| Ignore SSL Warning | Enable for lab clusters / the ONTAP Simulator that present a self-signed certificate. |

The certificate alias in Keyfactor Command maps to the ONTAP certificate **name**, which must be unique within a scope. A custom alias is required. Private keys are not permitted on this store type.

---

#### Supported Operations

| Operation | Behavior |
| :-------- | :------- |
| Inventory | Returns all `server_ca` and `client_ca` certificates in the store's scope. |
| Add | Installs a `server_ca` or `client_ca` public certificate into the scope, per the `CertType` entry parameter. If a trust anchor with the same name already exists in the scope, the Overwrite option must be set; the existing entry is removed and the new one installed in its place. |
| Remove | Deletes the named trust anchor from the scope. |
| Discovery | Enumerates the scopes on the cluster (cluster + each SVM) as candidate store paths. |

---

#### Managing pre-installed roots

A newly provisioned cluster ships with a large set of well-known public root CAs already present as `server_ca` certificates. These appear in inventory alongside any trust anchors you add, and that is expected.

Pre-installed roots are inventoried but **cannot be removed through this integration**. ONTAP blocks deletion of factory certificates server-side, returning an error that directs you to the ONTAP CLI. If a Remove job targets a pre-installed root, it fails with a clear message rather than causing any change. Trust anchors that you added through the integration can be removed normally.

> :information_source: Because ONTAP itself protects the built-in roots, there is no risk of the integration inadvertently deleting one. Removing or replacing a factory root, if genuinely required, is a deliberate CLI operation performed directly on the cluster.

---

#### Example

To manage the trust anchors on SVM `vs0` of a cluster reachable at `ontap.example.com`:

- **ClientMachine**: `ontap.example.com`
- **StorePath**: `vs0`
- **Server Username / Password**: the ONTAP service account credentials
- Add a public CA certificate with alias `corp-issuing-ca`, selecting `CertType = server_ca`, so ONTAP (as a client) will trust servers whose certificates chain to that CA.

To manage cluster-scoped trust anchors on the same cluster, create a second store with **StorePath** = `[cluster]`.

---

#### Troubleshooting

- **A duplicate-name Add fails**: trust anchor names are unique per scope. Enable Overwrite to replace an existing anchor of the same name, or choose a different alias.
- **Add rejected because a private key was supplied**: this store type manages public trust anchors only. Add certificates without private keys; use [ONTAP_CERTS](./ontap_certs.md) for certificates that carry a key.
- **Remove of a pre-installed root fails**: this is expected. ONTAP blocks deletion of factory-installed roots and directs you to the CLI; the integration surfaces that as a clear failure. Only trust anchors added through the integration can be removed here. See [Managing pre-installed roots](#managing-pre-installed-roots).
- **TLS/connection errors against a lab cluster or the Simulator**: enable **Ignore SSL Warning** on the store.
