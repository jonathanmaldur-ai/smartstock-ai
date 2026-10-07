# Jonathan Alves Ribeiro

**Analista de Suporte e Infraestrutura de TI** · Mogi Guaçu – SP
[LinkedIn](https://www.linkedin.com/in/jonathan-alves-ribeiro-95596a303) · [Currículo em PDF](curriculo-jonathan-alves-ribeiro.pdf)

Analista de Suporte com experiência em ambientes corporativos: suporte N1/N2, administração de usuários e acessos, gestão de chamados, segurança de endpoints e automação. Perfil analítico, focado em resolver incidentes na causa e em automatizar o que é repetitivo.

Objetivo: atuar como **Analista de Suporte / Infraestrutura de TI**, com foco em administração de sistemas, segurança da informação e suporte a usuários.

---

## O SmartStock AI na prática

O [SmartStock AI](README.md) é o projeto que mostra como eu trabalho do começo ao fim: levantei o problema com a operação, defini as regras e as prioridades, aprovei cada decisão (registradas por escrito, uma a uma), preparei e mantenho o servidor e validei cada entrega com os dados reais da empresa. O código foi desenvolvido com apoio de IA (Claude Code), sob a minha direção.

A parte de infraestrutura é toda minha responsabilidade:

| Área | O que foi feito |
|---|---|
| **Servidor Linux** | Ubuntu Server com PostgreSQL, Caddy (HTTPS com autoridade certificadora interna) e a aplicação como serviço systemd, com reinício automático. |
| **Acesso remoto seguro** | VPN para gestores fora da empresa, firewall liberando só a porta HTTPS para a VPN e o sistema sem nenhuma exposição na internet. |
| **Menor privilégio** | Automações sem senha guardada: chaves SSH restritas por rede de origem, sem terminal e presas a um único comando; sudo liberado só para scripts específicos do root. |
| **Automação** | Pasta de entrega com tarefa agendada no Windows (PowerShell): a planilha exportada do ERP é enviada, importada e analisada sozinha. Publicação de nova versão com um clique, com backup do banco e verificação de saúde. |
| **Backup e continuidade** | Backup diário do banco e do código em HD dedicado, com retenção; análise automática diária por timer do systemd, que roda assim que o servidor liga se ele estava desligado no horário. |
| **LGPD** | Dados pessoais de clientes e vendedores descartados na importação; auditoria de login, importações e decisões. |

---

## Competências técnicas

**Suporte & Infraestrutura**
- Suporte técnico N1 e N2 (presencial e remoto), Help Desk e Service Desk
- Gestão de ativos e troubleshooting avançado
- Servidores Linux (Ubuntu Server, systemd, SSH, firewall, scripts Bash) e Windows Server
- Automação com PowerShell, Bash e n8n

**Ferramentas e Plataformas**
- GLPI · ManageEngine · Jira · Deskbee · Slack · Notion
- Fleet (osquery) · CrowdStrike Falcon (EDR) · SIEM
- PostgreSQL · Caddy · Git

**Ambientes e Sistemas**
- Microsoft 365 (Exchange, Teams, SharePoint)
- Azure / Entra ID (gestão de usuários e acessos)
- Google Workspace

**Redes & Segurança**
- Redes TCP/IP, DNS, DHCP, Wi-Fi e VPN
- Segurança da informação: boas práticas, hardening, menor privilégio, certificados
- Monitoramento de endpoints e análise de incidentes
- GPO (Group Policy) e políticas de criptografia

---

## Experiência profissional

### Analista de Suporte de TI (Pleno) — SS3 Tecnologia
*02/2025 – 03/2026*

- Atendimento e resolução de chamados técnicos (N1 e N2)
- Administração de usuários e permissões via Azure (Entra ID)
- Suporte a Microsoft 365 e Google Workspace
- Gestão de chamados via GLPI, Jira e ManageEngine
- Monitoramento de segurança com CrowdStrike Falcon e SIEM; análise de eventos e resposta a incidentes
- Suporte a ambiente corporativo com redes e servidores Windows
- Automação e rotinas em Linux (scripts de monitoramento)

**Projetos em cliente (G4)**
- Implementação de criptografia em ambientes Linux
- Automação da instalação de softwares em Windows, macOS e Linux, padronizando o setup
- Automação e configuração de fluxos de chamados no Jira, com integração ao Slack para capturar prioridade e contexto
- Respostas automáticas e automações com n8n integrado ao Slack
- Inventário de ativos cruzando dados do ManageEngine e do CrowdStrike
- Auditoria de chamados no Jira, eliminando fluxos redundantes e reduzindo retrabalho
- Análise de chamados recorrentes com dados do CrowdStrike, mitigando incidentes desnecessários
- Documentação de processos de suporte N1 e N2

**Projetos em cliente (Inove)**
- Levantamento e adequação do inventário de ativos de TI
- Estruturação dos processos de facilities e suporte técnico
- Políticas de criptografia e regras de segurança via Kaspersky
- Aplicação e administração de políticas via GPO
- Administração do GLPI: grupos, permissões, alocação de colaboradores e fluxo de atendimento

### Analista de TI — Grupo Impacto
*08/2023 – 01/2025*

- Suporte técnico N1 e N2 e atendimento a usuários
- Instalação, configuração e manutenção de máquinas
- Apoio à equipe de infraestrutura

### Estagiário de TI — Laboratório Cristália
*01/2018 – 08/2018*

- Suporte técnico básico e manutenção de computadores e sistemas

---

## Formação

- **Redes de Computadores** — Faculdade Metropolitana (cursando)
- **Técnico em Informática** — ETEC João Maria Stevanatto (concluído)

## Certificações e cursos

- Segurança da Informação e Cybersecurity
- Linux Essentials
- Automação com n8n
- Linux – Criação de Scripts de Monitoramento de Sistema
- Linux – Gerenciamento de Diretórios, Arquivos e Processos
- Redes Onboarding: Uma Perspectiva Prática
