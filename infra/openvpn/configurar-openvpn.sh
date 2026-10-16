#!/usr/bin/env bash
# Acesso remoto ao SmartStock por uma VPN OpenVPN própria, sem expor a aplicação na internet.
#   sudo bash configurar-openvpn.sh ENDERECO_PUBLICO [IP_DO_SERVIDOR_NA_REDE]
#   ex.: sudo bash configurar-openvpn.sh vpn.empresa.com.br 192.168.1.10
#
# 1. Instala o OpenVPN e o Easy-RSA e cria a autoridade de certificados da VPN (só o root lê).
# 2. Servidor em UDP 1194 com AES-256-GCM e tls-crypt: sem o arquivo da pessoa, a porta nem responde.
# 3. Quem conecta recebe só a rota do IP do servidor (/32): chega ao SmartStock (porta 443) e a mais nada da rede.
#    O próprio servidor é a ponta da VPN, então não há encaminhamento nem NAT para a rede da empresa.
# 4. Firewall: abre UDP 1194 e libera só a 443 para quem chega pela VPN (tun0).
# 5. Instala o comando "smartstock-vpn" para criar, listar e revogar o acesso de cada pessoa.
#
# No roteador/modem: redirecione a porta UDP 1194 para o IP deste servidor.
# Para desfazer: sudo systemctl disable --now openvpn-server@server; sudo ufw delete allow 1194/udp;
#                sudo ufw delete allow in on tun0 to any port 443 proto tcp

set -euo pipefail

PUBLIC_ENDPOINT=${1:?Informe o IP público fixo ou o nome DNS da empresa}
SERVER_IP=${2:-$(hostname -I | cut -d" " -f1)}
VPN_NET=10.8.0.0
PORT=1194
DIR=/etc/openvpn/server
PKI_DIR=$DIR/easy-rsa
SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)

step() { echo; echo "=== $* ==="; }
ok() { echo "[ok] $*"; }
fail() { echo "[erro] $*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || fail "Execute com sudo."
[[ -f "$SCRIPT_DIR/vpn-usuario.sh" ]] || fail "vpn-usuario.sh precisa estar na mesma pasta deste script."
[[ "$PUBLIC_ENDPOINT" =~ ^[A-Za-z0-9.-]+$ ]] || fail "Endereço público inválido: $PUBLIC_ENDPOINT"

step "Instalando OpenVPN e Easy-RSA"
apt-get update -qq
apt-get install -y -qq openvpn easy-rsa
ok "$(openvpn --version | head -n1 | cut -d' ' -f1-2)"

step "Autoridade de certificados da VPN"
export EASYRSA_ALGO=ec EASYRSA_CURVE=secp384r1 EASYRSA_CRL_DAYS=3650 EASYRSA_CERT_EXPIRE=1095
if [[ -f "$PKI_DIR/pki/ca.crt" ]]; then
    ok "Já existe; mantida (os acessos já criados continuam valendo)."
else
    rm -rf "$PKI_DIR"
    make-cadir "$PKI_DIR"
    chmod 700 "$PKI_DIR"
    cd "$PKI_DIR"
    ./easyrsa --batch init-pki >/dev/null
    ./easyrsa --batch --req-cn="SmartStock VPN CA" build-ca nopass >/dev/null
    ./easyrsa --batch build-server-full server nopass >/dev/null
    ./easyrsa --batch gen-crl >/dev/null
    ok "Criada (certificados de pessoas valem 3 anos)."
fi
cd "$PKI_DIR"
install -m 644 pki/ca.crt "$DIR/ca.crt"
install -m 644 pki/issued/server.crt "$DIR/server.crt"
install -m 600 pki/private/server.key "$DIR/server.key"
install -m 644 pki/crl.pem "$DIR/crl.pem"
[[ -f "$DIR/tc.key" ]] || openvpn --genkey secret "$DIR/tc.key"
chmod 600 "$DIR/tc.key"

step "Configuração do servidor"
cat > "$DIR/server.conf" <<EOF
# SmartStock AI - VPN. Gerado por configurar-openvpn.sh.
port $PORT
proto udp
dev tun
ca $DIR/ca.crt
cert $DIR/server.crt
key $DIR/server.key
dh none
tls-crypt $DIR/tc.key
crl-verify $DIR/crl.pem
tls-version-min 1.2
data-ciphers AES-256-GCM:CHACHA20-POLY1305
topology subnet
server $VPN_NET 255.255.255.0
push "route $SERVER_IP 255.255.255.255"
ifconfig-pool-persist $DIR/ipp.txt
keepalive 10 60
max-clients 50
user nobody
group nogroup
persist-key
persist-tun
status /var/log/openvpn/status.log
status-version 2
verb 3
explicit-exit-notify 1
EOF
mkdir -p /var/log/openvpn
# Dados que o smartstock-vpn usa para montar o arquivo de cada pessoa.
printf 'ENDPOINT=%s\nPORT=%s\n' "$PUBLIC_ENDPOINT" "$PORT" > "$DIR/endpoint.conf"
chmod 600 "$DIR/endpoint.conf"
ok "Endereço público para as pessoas: $PUBLIC_ENDPOINT:$PORT (UDP)."

step "Firewall"
ufw allow $PORT/udp comment 'OpenVPN SmartStock' >/dev/null
ufw allow in on tun0 to any port 443 proto tcp comment 'SmartStock pela OpenVPN' >/dev/null
ok "UDP $PORT aberta; pela VPN só a 443 do SmartStock."

step "Iniciando"
systemctl enable --now openvpn-server@server >/dev/null
systemctl restart openvpn-server@server
sleep 2
systemctl is-active --quiet openvpn-server@server \
    || { journalctl -u openvpn-server@server -n 30 --no-pager; fail "O OpenVPN não subiu (log acima)."; }
ok "OpenVPN rodando (reinicia sozinho com o servidor)."

install -m 755 -o root -g root "$SCRIPT_DIR/vpn-usuario.sh" /usr/local/sbin/smartstock-vpn
ok "Comando instalado: smartstock-vpn"

echo
echo "Próximo passo: crie o seu acesso de teste:"
echo "  sudo smartstock-vpn criar maria"
