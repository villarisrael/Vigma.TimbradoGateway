```mermaid
flowchart TB

%% Nivel 1 - Core
subgraph CORE["VIGMA CORE INFRASTRUCTURE"]
    TENANT["Tenant Registry"]
    IAM["Identity & Access"]
    EVENTS["Event Bus / Event Log"]
    OBS["Observability & Logs"]
    CONFIG["Tenant Configuration"]
end

%% Nivel 2 - Módulos
subgraph MODULES["VIGMA PLATFORM MODULES"]
    GATEWAY["Vigma Timbrado Gateway"]
    PAYMENT["Vigma PaymentHub"]
    AQUA["Aqua Core"]
    CERT["Certificados de Autenticidad"]
    SUPPORT["Soporte Hub (Futuro)"]
end

%% Proveedores de pago
subgraph PROVIDERS["Payment Providers"]
    BANORTE["Banorte"]
    BANAMEX["Banamex"]
    STRIPE["Stripe"]
end

%% Integraciones externas
subgraph EXTERNAL["External Systems"]
    PAC["PAC"]
    SIAAPI["SIAAPI (Cliente externo)"]
    AGENT["Agent Bridge (On-Prem)"]
    USERS["Usuarios / Ciudadanos"]
end

%% Conexiones Core
TENANT --> GATEWAY
TENANT --> PAYMENT
TENANT --> AQUA
TENANT --> CERT

IAM --> GATEWAY
IAM --> PAYMENT
IAM --> AQUA

EVENTS --> PAYMENT
EVENTS --> GATEWAY

%% Gateway
GATEWAY --> PAC
SIAAPI --> GATEWAY

%% PaymentHub
USERS --> PAYMENT
PAYMENT --> BANORTE
PAYMENT --> BANAMEX
PAYMENT --> STRIPE

BANORTE --> PAYMENT
BANAMEX --> PAYMENT
STRIPE --> PAYMENT

PAYMENT --> GATEWAY
PAYMENT --> AGENT

%% Infraestructura
PAYMENT -.-> OBS
GATEWAY -.-> OBS
AQUA -.-> OBS
CERT -.-> OBS

