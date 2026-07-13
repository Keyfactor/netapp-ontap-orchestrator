## Overview

The ONTAP_CERTS certificate store type manages **end-entity and signer certificates** on an ONTAP cluster or SVM — that is, ONTAP certificates that carry a private key. These are the `server` certificates ONTAP presents when acting as an SSL server (management, NAS-over-TLS, and similar endpoints) and the `client` certificates ONTAP presents when acting as an SSL client to an external service.

For trust anchors (public CA certificates with no private key), use the [ONTAP_TRUSTED](./ontap_trusted.md) store type instead.

## Requirements

1. [Certificate Format](#certificate-format)
1. [Scope and the Store Path](#scope-and-the-store-path)
1. [Configuring the Certificate Store](#configuring-the-certificate-store)
1. [Supported Operations](#supported-operations)
1. [Example](#example)
1. [Troubleshooting](#troubleshooting)

---

#### Certificate Format

Certificates in this store are ONTAP certificates of type `server` or `client`, each consisting of a certificate (with its issuer chain) and a private key.

When adding a certificate from Keyfactor Command, the certificate and private key are installed together against the target scope. Because these are end-entity certificates, a private key is **required** on Add.

The `CertType` entry parameter selects which ONTAP type to install the entry as:

| CertType | Meaning |
| :------- | :------ |
| `server` | Certificate + private key used by ONTAP acting as an SSL **server**. |
| `client` | Certificate + private key used by ONTAP acting as an SSL **client**. |

> :warning: The private key of a certificate already present on the cluster **cannot** be retrieved through the ONTAP API. Inventory returns each certificate's metadata and public certificate only. This store type is therefore best used to *place* Command-managed certificates onto ONTAP and keep them current, rather than to import existing key material off the cluster.

---

#### Scope and the Store Path

The **Store Path** identifies the scope whose certificates the store manages:
- an **SVM name** (for example `vs0`) to manage that SVM's certificates, or
- the literal token **`[cluster]`** (square brackets included) to manage cluster-scoped certificates.

The **ClientMachine** is the cluster management hostname or IP; requests are issued against `https://<ClientMachine>/api`. See the [Overview](./content.md#scope-cluster-vs-svm) for a full explanation of scope, including why the bracketed `[cluster]` token is used.

---

#### Configuring the Certificate Store

| Field | Description |
| :---- | :---------- |
| ClientMachine | The ONTAP cluster management hostname or IP address. |
| StorePath | The scope: an SVM name, or `[cluster]` for cluster scope. |
| Server Username | ONTAP service account username (PAM-eligible). |
| Server Password | ONTAP service account password (PAM-eligible). |
| Ignore SSL Warning | Enable for lab clusters / the ONTAP Simulator that present a self-signed certificate. |

The certificate alias in Keyfactor Command maps to the ONTAP certificate **name**, which must be unique within a scope. A custom alias is required.

---

#### Supported Operations

| Operation | Behavior |
| :-------- | :------- |
| Inventory | Returns all `server` and `client` certificates in the store's scope. |
| Add | Installs a `server` or `client` certificate (with private key) into the scope, per the `CertType` entry parameter. If a certificate with the same name already exists in the scope, the Overwrite option must be set; the existing certificate is removed and the new one installed in its place. |
| Remove | Deletes the named certificate from the scope. |
| Discovery | Enumerates the scopes on the cluster (cluster + each SVM) as candidate store paths. |

> :warning: ONTAP has no in-place certificate "renew" operation. Renewal is performed as a replace: the new certificate is installed and the old one removed.

---

#### Example

To manage the certificates on SVM `vs0` of a cluster reachable at `ontap.example.com`:

- **ClientMachine**: `ontap.example.com`
- **StorePath**: `vs0`
- **Server Username / Password**: the ONTAP service account credentials
- Add a certificate with alias `web-tls`, selecting `CertType = server`, to install it as a server certificate on `vs0`.

To manage cluster-scoped certificates on the same cluster, create a second store with **StorePath** = `[cluster]`.

---

#### Troubleshooting

- **A duplicate-name Add fails**: certificate names are unique per scope. Enable Overwrite to replace an existing certificate of the same name, or choose a different alias.
- **TLS/connection errors against a lab cluster or the Simulator**: enable **Ignore SSL Warning** on the store.
- **Add rejected for `root_ca`**: local signing CAs are not installed via this integration; they must be generated on the appliance and are out of scope for this release.
- **Certificate lands in the wrong place**: confirm the Store Path. Cluster scope (`[cluster]`) and an SVM scope are independent stores; installing to one does not affect the other.
