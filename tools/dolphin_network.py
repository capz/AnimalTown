"""Match Dolphin's DNS forwarding to the console SDK's resolver.

Dolphin 2503 sends redirected UDP replies from the configured resolver's real
address. Its default differs from NetworkOptions.DnsServers, so a validating
guest discards those replies. Keep both sides on 8.8.8.8 and probe that exact
route before starting the public console scenario.
"""
import argparse
import secrets
import socket
import struct

DNS_SERVER = '8.8.8.8'


def online_core_settings():
    return f'SerialPort1 = 12\nBBA_BUILTIN_DNS = {DNS_SERVER}'


def probe(host, timeout=5):
    labels = host.rstrip('.').split('.')
    if not labels or any(not label or len(label) > 63 or
                         any(c not in 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-' for c in label)
                         for label in labels) or len(host) > 253:
        raise ValueError('Invalid DNS probe hostname')
    identity = secrets.randbits(16)
    question = b''.join(bytes([len(label)]) + label.encode('ascii') for label in labels) + b'\0\0\1\0\1'
    query = struct.pack('!6H', identity, 0x0100, 1, 0, 0, 0) + question
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as udp:
        udp.settimeout(timeout)
        udp.connect((DNS_SERVER, 53))
        udp.send(query)
        reply = udp.recv(4096)
    if len(reply) < 12:
        raise RuntimeError('DNS probe returned a truncated header')
    received, flags, questions, answers, _, _ = struct.unpack('!6H', reply[:12])
    if received != identity or flags & 0xfa0f != 0x8000 or questions != 1 or answers == 0:
        raise RuntimeError('DNS probe returned an unusable IPv4 answer')
    print(f'Dolphin DNS preflight passed: resolver {DNS_SERVER}, {answers} answer records', flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--probe', required=True, metavar='HOST')
    args = parser.parse_args()
    probe(args.probe)
