#!/usr/bin/env bash
# Acessos da VPN do SmartStock Instalado pelo configurar-openvpn.sh como /usr/local/sbin/smartstock-vpn.
#   sudo smartstock-vpn criar NOME     cria o acesso e o arquivo NOME.ovpn (pede uma senha para o arquivo)
#   sudo smartstock-vpn listar         mostra os acessos válidos, revogados e quem está conectado agora
#   sudo smartstock-vpn revogar NOME   corta o acesso na hora (ex.: pessoa saiu da empresa)
#
# O arquivo .ovpn é a "chave" da pessoa: fica em /home/<quem rodou o sudo>/vpn/ e é protegido pela senha
# escolhida aqui. Entregue o arquivo e a senha por meios separados (ex.: arquivo em pendrive, senha falada).

set -euo pipefail

DIR=/etc/openvpn/server
PKI_DIR=$DIR/easy-rsa
export EASYRSA_ALGO=ec EASYRSA_CURVE=secp384r1 EASYRSA_CRL_DAYS=3650 EASYRSA_CERT_EXPIRE=1095

ok() { echo "[ok] $*"; }
fail() { echo "[erro] $*" >&2; exit 1; }
usage() { sed -n '3,5p' "$0" | sed 's/^# //'; exit 1; }

[[ $EUID -eq 0 ]] || fail "Execute com sudo."
[[ -f "$PKI_DIR/pki/ca.crt" ]] || fail "VPN não configurada. Rode antes o configurar-openvpn.sh."

action=${1:-}
name=${2:-}
if [[ "$action" == criar || "$action" == revogar ]]; then
    [[ "$name" =~ ^[a-z0-9][a-z0-9._-]{1,30}$ ]] || fail "Nome inválido: use letras minúsculas, números, ponto ou hífen (ex.: maria)."
fi
cd "$PKI_DIR"

refresh_crl() {
    ./easyrsa --batch gen-crl >/dev/null
    install -m 644 pki/crl.pem "$DIR/crl.pem"
}

create() {
    [[ ! -f "pki/issued/$name.crt" ]] || fail "Já existe um acesso '$name'. Para refazer: revogar e criar com outro nome (ex.: $name-2)."
    echo "Escolha a SENHA do arquivo de $name (pedida duas vezes; a pessoa digita ao conectar):"
    ./easyrsa --batch build-client-full "$name"

    # shellcheck source=/dev/null
    . "$DIR/endpoint.conf"
    local owner=${SUDO_USER:-root}
    local home
    home=$(getent passwd "$owner" | cut -d: -f6)
    local out="$home/vpn/$name.ovpn"
    install -d -m 700 -o "$owner" -g "$owner" "$home/vpn"
    {
        cat <<EOF
# SmartStock AI - acesso VPN de $name ($(date +%d/%m/%Y))
client
dev tun
proto udp
remote $ENDPOINT $PORT
resolv-retry infinite
nobind
persist-key
persist-tun
remote-cert-tls server
data-ciphers AES-256-GCM:CHACHA20-POLY1305
verb 3
EOF
        echo "<ca>";        cat pki/ca.crt;                                  echo "</ca>"
        echo "<cert>";      sed -n '/BEGIN CERTIFICATE/,/END CERTIFICATE/p' "pki/issued/$name.crt"; echo "</cert>"
        echo "<key>";       cat "pki/private/$name.key";                     echo "</key>"
        echo "<tls-crypt>"; cat "$DIR/tc.key";                               echo "</tls-crypt>"
    } > "$out"
    chown "$owner:$owner" "$out"
    chmod 600 "$out"
    ok "Acesso criado: $out"
    echo "Para trazer ao seu PC (no PowerShell): scp $owner@$(hostname -I | cut -d" " -f1):vpn/$name.ovpn ."
    echo "Depois de entregar, apague a cópia do servidor: rm ~/vpn/$name.ovpn"
}

revoke() {
    [[ -f "pki/issued/$name.crt" ]] || fail "Acesso '$name' não encontrado (veja: sudo smartstock-vpn listar)."
    ./easyrsa --batch revoke "$name" >/dev/null
    refresh_crl
    # Derruba uma conexão que esteja aberta agora; a nova lista de revogados impede que volte.
    systemctl restart openvpn-server@server
    ok "Acesso '$name' revogado. A pessoa não conecta mais."
}

list() {
    echo "Acessos (V = válido, R = revogado, E = expirado):"
    awk -F'\t' '$6 !~ /CN=server$/ { split($6, cn, "CN="); printf "  %s  %-25s validade %s\n", $1, cn[2], substr($2,1,6) }' pki/index.txt
    echo
    echo "Conectados agora:"
    if [[ -f /var/log/openvpn/status.log ]]; then
        awk -F, '/^CLIENT_LIST/ { printf "  %-25s desde %s\n", $2, $8 }' /var/log/openvpn/status.log
    fi
}

case "$action" in
    criar) create ;;
    revogar) revoke ;;
    listar) list ;;
    *) usage ;;
esac
