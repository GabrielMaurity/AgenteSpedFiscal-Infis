#!/usr/bin/env python3
"""Gera e confere uma EFD ICMS/IPI fictícia e completa, conforme o Guia Prático 3.2.4 e a NT 2026.001.

Uso: python tools/gerar-fixtures-efd/gerar_ficticio_completo.py

Destino: tests/fixtures/efd/ficticio-completo-2026-01.txt

Todos os dados são fictícios. Os dígitos verificadores são calculados, mas os cadastros não foram
consultados, e as alíquotas e o código de receita servem só para exercitar o arquivo: não representam
a legislação do RJ ou de SP. Depois de gravar, conferir() relê o arquivo e recalcula tudo a partir do texto.

Cenário: comércio atacadista no RJ (perfil A, IND_ATIV = 1), período de janeiro de 2026.
- Entradas de terceiros: NF-e com C170 e C190 (interestadual, interna, ST e uso e consumo).
- Saídas de emissão própria: NF-e só com C190 (Guia, C100, Exceção 2) e uma NF-e cancelada (Exceção 1).
- Apuração: E100, E110 e E116, com saldo devedor.
"""
import json
import pathlib
import sys
from collections import defaultdict
from decimal import Decimal

RAIZ = pathlib.Path(__file__).resolve().parents[2]
DESTINO = RAIZ / "tests/fixtures/efd/ficticio-completo-2026-01.txt"

DT_INI, DT_FIN = "01012026", "31012026"
DOIS = Decimal("0.01")


def valor(numero):
    return f"{Decimal(numero).quantize(DOIS):.2f}".replace(".", ",")


def quantidade(numero):
    return f"{Decimal(numero):.5f}".replace(".", ",")


def numero(texto):
    return Decimal(texto.replace(",", ".")) if texto else Decimal(0)


# ---- Dígitos verificadores ----------------------------------------------------------------------

def dv_modulo11(digitos, pesos):
    resto = sum(int(d) * p for d, p in zip(digitos, pesos)) % 11
    return "0" if resto < 2 else str(11 - resto)


def cnpj(base12):
    primeiro = dv_modulo11(base12, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])
    return base12 + primeiro + dv_modulo11(base12 + primeiro, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])


def cpf(base9):
    primeiro = dv_modulo11(base9, range(10, 1, -1))
    return base9 + primeiro + dv_modulo11(base9 + primeiro, range(11, 1, -1))


def ie_rj(base7):
    return base7 + dv_modulo11(base7, [2, 7, 6, 5, 4, 3, 2])


def ie_sp(base8, meio2):
    pesos9 = [1, 3, 4, 5, 6, 7, 8, 10]
    d9 = str(sum(int(d) * p for d, p in zip(base8, pesos9)) % 11)[-1]
    parcial = base8 + d9 + meio2
    d12 = str(sum(int(d) * p for d, p in zip(parcial, [3, 2, 10, 9, 8, 7, 6, 5, 4, 3, 2])) % 11)[-1]
    return parcial + d12


def dv_chave(chave43):
    soma = sum(int(d) * (2 + i % 8) for i, d in enumerate(reversed(chave43)))
    resto = soma % 11
    return "0" if resto < 2 else str(11 - resto)


def chave_nfe(uf, aamm, cnpj_emitente, serie, numero_doc, codigo):
    parcial = f"{uf}{aamm}{cnpj_emitente}55{serie}{numero_doc:09d}1{codigo:08d}"
    return parcial + dv_chave(parcial)


# ---- Cadastros ----------------------------------------------------------------------------------

CNPJ_EMPRESA = cnpj("123456780001")
CNPJ_FORN_SP = cnpj("112223330001")
CNPJ_FORN_RJ = cnpj("114447770001")
CNPJ_CLI_RJ = cnpj("223334440001")
CNPJ_CLI_SP = cnpj("334445550001")

PARTICIPANTES = [
    ("FORNSP01", "FORNECEDOR PAULISTA COMÉRCIO LTDA", CNPJ_FORN_SP, ie_sp("11022033", "04"), "3550308", "AVENIDA FICTÍCIA", "1000", "SÉ"),
    ("FORNRJ01", "DISTRIBUIDORA FLUMINENSE DE TESTE LTDA", CNPJ_FORN_RJ, ie_rj("7654321"), "3304557", "RUA DOS FORNECEDORES", "200", "CENTRO"),
    ("CLIRJ001", "CLIENTE TESTE S/A", CNPJ_CLI_RJ, ie_rj("2345678"), "3304557", "RUA DO CLIENTE", "500", "BAIRRO"),
    ("CLISP001", "CLIENTE PAULISTA DE TESTE LTDA", CNPJ_CLI_SP, ie_sp("22033044", "05"), "3550308", "RUA DAS OPERAÇÕES", "75", "BELA VISTA"),
]

UNIDADES = [("KG", "QUILOGRAMA"), ("UN", "UNIDADE")]

# COD_ITEM, descrição, unidade, TIPO_ITEM, NCM (dispensado para uso e consumo), ALIQ_ICMS interna.
# Os NCM não foram conferidos na tabela oficial, ainda pendente no projeto.
ITENS = [
    ("PROD001", "PRODUTO TESTE SPED 2026", "UN", "00", "32041700", "18,00"),
    ("PROD002", "CANETA FICTÍCIA PARA REVENDA", "UN", "00", "96081000", "18,00"),
    ("PROD003", "MERCADORIA FICTÍCIA COM ICMS RETIDO POR ST", "KG", "00", "32081010", "18,00"),
    ("USO0001", "MATERIAL DE ESCRITÓRIO PARA USO E CONSUMO", "UN", "07", "", ""),
]

NATUREZAS = [("COMPRA", "COMPRA DE MERCADORIA PARA REVENDA"), ("USOCONS", "AQUISIÇÃO PARA USO E CONSUMO")]

# item, quantidade, valor, CST, CFOP, alíquota, COD_NAT
ENTRADAS = [
    {"part": "FORNSP01", "uf": "35", "cnpj": CNPJ_FORN_SP, "num": 4501, "cod": 10004501, "emi": "05012026", "ent": "08012026", "pgto": "1", "frt": "0",
     "itens": [("PROD001", "100", "2500.00", "000", "2102", "12.00", "COMPRA"),
               ("PROD002", "50", "2000.00", "000", "2102", "12.00", "COMPRA")]},
    {"part": "FORNRJ01", "uf": "33", "cnpj": CNPJ_FORN_RJ, "num": 7801, "cod": 10007801, "emi": "12012026", "ent": "12012026", "pgto": "0", "frt": "0",
     "itens": [("PROD001", "40", "1200.00", "000", "1102", "18.00", "COMPRA"),
               ("PROD003", "200", "1000.00", "060", "1403", "0.00", "COMPRA")]},
    {"part": "FORNRJ01", "uf": "33", "cnpj": CNPJ_FORN_RJ, "num": 7815, "cod": 10007815, "emi": "19012026", "ent": "20012026", "pgto": "0", "frt": "9",
     "itens": [("USO0001", "10", "125.00", "090", "1556", "0.00", "USOCONS")]},
]

# Saídas de emissão própria: só C100 e C190. (CST, CFOP, alíquota, valor da operação)
SAIDAS = [
    {"part": "CLIRJ001", "num": 1001, "cod": 20001001, "emi": "15012026", "sai": "15012026", "pgto": "1", "frt": "0",
     "analiticos": [("000", "5102", "18.00", "3000.00"), ("060", "5405", "0.00", "600.00")]},
    {"part": "CLISP001", "num": 1002, "cod": 20001002, "emi": "22012026", "sai": "23012026", "pgto": "1", "frt": "0",
     "analiticos": [("000", "6102", "12.00", "2500.00")]},
]
CANCELADA = {"num": 1003, "cod": 20001003}


def icms(base, aliquota):
    return (Decimal(base) * Decimal(aliquota) / 100).quantize(DOIS)


def c100(ind_oper, ind_emit, part, sit, num, chave, dt_doc, dt_es, vl_doc, pgto, vl_merc, frt, bc, vl_icms):
    return ["C100", ind_oper, ind_emit, part, "55", sit, "001", str(num), chave, dt_doc, dt_es,
            valor(vl_doc), pgto, "0,00", "0,00", valor(vl_merc), frt, "0,00", "0,00", "0,00",
            valor(bc), valor(vl_icms), "0,00", "0,00", "0,00", "", "", "", ""]


def c190(cst, cfop, aliquota, operacao, bc, vl_icms):
    return ["C190", cst, cfop, valor(aliquota), valor(operacao), valor(bc), valor(vl_icms), "0,00", "0,00", "0,00", "0,00", ""]


def montar_bloco_c():
    linhas = [["C001", "0"]]
    creditos = debitos = Decimal(0)

    for nota in ENTRADAS:
        grupos = defaultdict(lambda: [Decimal(0), Decimal(0), Decimal(0)])
        itens = []
        for i, (item, qtd, vl, cst, cfop, aliq, nat) in enumerate(nota["itens"], 1):
            unidade = next(u[2] for u in ITENS if u[0] == item)
            # Só há crédito para CST 000; CST 060 e 090 ficam sem base e sem imposto (Guia, C170).
            base = Decimal(vl) if cst == "000" else Decimal(0)
            imposto = icms(base, aliq)
            itens.append(["C170", str(i), item, "", quantidade(qtd), unidade, valor(vl), "0,00", "0", cst, cfop, nat,
                          valor(base), valor(aliq), valor(imposto), "0,00", "0,00", "0,00"] + [""] * 20)
            grupo = grupos[(cst, cfop, aliq)]
            grupo[0] += Decimal(vl)
            grupo[1] += base
            grupo[2] += imposto
        total = sum(Decimal(i[2]) for i in nota["itens"])
        bc_total = sum(g[1] for g in grupos.values())
        icms_total = sum(g[2] for g in grupos.values())
        chave = chave_nfe(nota["uf"], "2601", nota["cnpj"], "001", nota["num"], nota["cod"])
        linhas.append(c100("0", "1", nota["part"], "00", nota["num"], chave, nota["emi"], nota["ent"], total, nota["pgto"], total, nota["frt"], bc_total, icms_total))
        linhas += itens
        for (cst, cfop, aliq), (operacao, bc, imposto) in sorted(grupos.items()):
            linhas.append(c190(cst, cfop, aliq, operacao, bc, imposto))
        creditos += icms_total

    for nota in SAIDAS:
        chave = chave_nfe("33", "2601", CNPJ_EMPRESA, "001", nota["num"], nota["cod"])
        analiticos = []
        for cst, cfop, aliq, operacao in nota["analiticos"]:
            base = Decimal(operacao) if cst == "000" else Decimal(0)
            analiticos.append(c190(cst, cfop, aliq, operacao, base, icms(base, aliq)))
        total = sum(Decimal(a[3]) for a in nota["analiticos"])
        bc_total = sum(numero(a[5]) for a in analiticos)
        icms_total = sum(numero(a[6]) for a in analiticos)
        linhas.append(c100("1", "0", nota["part"], "00", nota["num"], chave, nota["emi"], nota["sai"], total, nota["pgto"], total, nota["frt"], bc_total, icms_total))
        linhas += analiticos
        debitos += icms_total

    # Exceção 1: NF-e própria cancelada traz só REG, IND_OPER, IND_EMIT, COD_MOD, COD_SIT, SER, NUM_DOC e CHV_NFE, sem filhos.
    chave = chave_nfe("33", "2601", CNPJ_EMPRESA, "001", CANCELADA["num"], CANCELADA["cod"])
    linhas.append(["C100", "1", "0", "", "55", "02", "001", str(CANCELADA["num"]), chave] + [""] * 20)

    linhas.append(["C990", "0"])
    return linhas, debitos, creditos


def montar_bloco_e(debitos, creditos):
    saldo = debitos - creditos
    apurado, transportar = (saldo, Decimal(0)) if saldo >= 0 else (Decimal(0), -saldo)
    linhas = [["E001", "0"], ["E100", DT_INI, DT_FIN],
              ["E110", valor(debitos), "0,00", "0,00", "0,00", valor(creditos), "0,00", "0,00", "0,00", "0,00",
               valor(apurado), "0,00", valor(apurado), valor(transportar), "0,00"]]
    if apurado > 0:
        linhas.append(["E116", "000", valor(apurado), "10022026", "FICT001", "", "", "", "", "012026"])
    linhas.append(["E990", "0"])
    return linhas


def montar():
    corpo = [
        ["0000", "020", "0", DT_INI, DT_FIN, "EMPRESA DE TESTE LTDA", CNPJ_EMPRESA, "", "RJ", ie_rj("1234567"), "3304557", "", "", "A", "1"],
        ["0001", "0"],
        ["0005", "EMPRESA TESTE", "20000000", "RUA DOS TESTES", "100", "", "CENTRO", "2199999999", "", "CONTATO@EMPRESA-TESTE.EXAMPLE"],
        ["0100", "CONTADOR DE TESTE", cpf("111222333"), "1RJ123456", "", "20000000", "RUA DO CONTADOR", "50", "", "CENTRO", "21988888888", "", "CONTADOR@EMPRESA-TESTE.EXAMPLE", "3304557"],
    ]
    for cod, nome, doc, ie, mun, end, num, bairro in PARTICIPANTES:
        corpo.append(["0150", cod, nome, "1058", doc, "", ie, mun, "", end, num, "", bairro])
    corpo += [["0190", u, d] for u, d in UNIDADES]
    for cod, descr, unidade, tipo, ncm, aliq in ITENS:
        corpo.append(["0200", cod, descr, "", "", unidade, tipo, ncm, "", "", "", aliq, ""])
    corpo += [["0400", c, d] for c, d in NATUREZAS]
    corpo.append(["0990", "0"])

    corpo += [["B001", "1"], ["B990", "0"]]  # Fora do DF: só abertura e encerramento (Guia, B001).
    bloco_c, debitos, creditos = montar_bloco_c()
    corpo += bloco_c
    corpo += [["D001", "1"], ["D990", "0"]]
    corpo += montar_bloco_e(debitos, creditos)
    corpo += [["G001", "1"], ["G990", "0"]]
    corpo += [["H001", "1"], ["H990", "0"]]  # Janeiro: o inventário de 31/12 pode ir em fevereiro (Guia, H001).
    corpo += [["K001", "1"], ["K990", "0"]]
    corpo += [["1001", "0"], ["1010"] + ["N"] * 13, ["1990", "0"]]
    return fechar(corpo)


def fechar(corpo):
    """Totalizadores: o 0000 soma no bloco 0 (Guia, 0990) e o 9999 soma no bloco 9 (Guia, 9990)."""
    por_bloco = defaultdict(int)
    contagem = defaultdict(int)
    for campos in corpo:
        por_bloco[campos[0][0]] += 1
        contagem[campos[0]] += 1
    for campos in corpo:
        if campos[0].endswith("990"):
            campos[1] = str(por_bloco[campos[0][0]])

    contagem.update({"9001": 1, "9990": 1, "9999": 1})
    contagem["9900"] = len(contagem) + 1
    linhas9 = [["9900", r, str(q)] for r, q in contagem.items()]
    qtd_bloco9 = 1 + len(linhas9) + 1 + 1
    return corpo + [["9001", "0"]] + linhas9 + [["9990", str(qtd_bloco9)], ["9999", str(len(corpo) + qtd_bloco9)]]


# ---- Conferência independente: lê o arquivo gravado e não usa os valores calculados acima ---------

QTD_CAMPOS = {"0000": 15, "0001": 2, "0005": 10, "0100": 14, "0150": 13, "0190": 3, "0200": 13, "0400": 3, "C100": 29,
              "C170": 38, "C190": 12, "E100": 3, "E110": 15, "E116": 10, "1010": 14, "9900": 3}


def conferir(caminho):
    erros = []
    bruto = caminho.read_bytes()
    texto = bruto.decode("latin-1")
    if bruto.count(b"\n") != bruto.count(b"\r\n") or not bruto.endswith(b"\r\n"):
        erros.append("Há linha sem CR e LF.")
    linhas = texto.split("\r\n")[:-1]
    registros = []
    for n, linha in enumerate(linhas, 1):
        if len(linha) < 2 or linha[0] != "|" or linha[-1] != "|":
            erros.append(f"Linha {n} sem delimitadores.")
            continue
        registros.append(linha[1:-1].split("|"))

    def por(codigo):
        return [r for r in registros if r[0] == codigo]

    if registros[0][0] != "0000" or registros[-1][0] != "9999":
        erros.append("O arquivo não vai de 0000 a 9999.")
    for r in registros:
        esperado = QTD_CAMPOS.get(r[0], 2)
        if len(r) != esperado:
            erros.append(f"{r[0]} tem {len(r)} campos; esperado {esperado}.")

    # Totalizadores.
    blocos = defaultdict(int)
    tipos = defaultdict(int)
    for r in registros:
        blocos[r[0][0]] += 1
        tipos[r[0]] += 1
    for r in registros:
        if r[0].endswith("990") and int(r[1]) != blocos[r[0][0]]:
            erros.append(f"{r[0]} informa {r[1]}, o bloco tem {blocos[r[0][0]]}.")
    declarados = {r[1]: int(r[2]) for r in por("9900")}
    if declarados != dict(tipos):
        erros.append("O 9900 diverge da contagem real.")
    if int(por("9999")[0][1]) != len(linhas):
        erros.append("O 9999 diverge do total de linhas.")

    # Dígitos verificadores.
    abertura = por("0000")[0]
    for doc in [abertura[6]] + [r[4] for r in por("0150")]:
        if cnpj(doc[:12]) != doc:
            erros.append(f"DV de CNPJ inválido: {doc}")
    if cpf(por("0100")[0][2][:9]) != por("0100")[0][2]:
        erros.append("DV do CPF inválido.")
    for ie, municipio in [(abertura[9], abertura[10])] + [(r[6], r[7]) for r in por("0150")]:
        calculada = ie_rj(ie[:7]) if municipio.startswith("33") else ie_sp(ie[:8], ie[9:11])
        if ie != calculada:
            erros.append(f"DV de IE inválido: {ie}")

    # Documentos.
    participantes = {r[1]: r for r in por("0150")}
    itens = {r[1]: r for r in por("0200")}
    unidades = {r[1] for r in por("0190")}
    naturezas = {r[1] for r in por("0400")}
    usados = {"part": set(), "item": set(), "unid": set(), "nat": set()}
    debitos = creditos = Decimal(0)
    atual = None
    documentos = []
    for r in registros:
        if r[0] == "C100":
            atual = {"c100": r, "c170": [], "c190": []}
            documentos.append(atual)
        elif r[0] == "C170":
            atual["c170"].append(r)
        elif r[0] == "C190":
            atual["c190"].append(r)
        elif r[0] == "C990":
            atual = None
    if not documentos:
        erros.append("Sem documentos no bloco C.")

    for d in documentos:
        n = d["c100"]
        ident = f"NF {n[7]}"
        chave = n[8]
        if len(chave) != 44 or dv_chave(chave[:43]) != chave[43]:
            erros.append(f"{ident}: DV da chave inválido.")
        if chave[20:22] != n[4] or chave[22:25] != n[6] or int(chave[25:34]) != int(n[7]):
            erros.append(f"{ident}: chave não bate com modelo, série ou número.")
        if n[2] == "0":
            if chave[6:14] != abertura[6][:8] or chave[:2] != "33":
                erros.append(f"{ident}: emissão própria com CNPJ base ou UF divergente na chave.")
            if d["c170"]:
                erros.append(f"{ident}: emissão própria não deve ter C170 (Exceção 2).")
        if n[5] == "02":
            if d["c170"] or d["c190"] or any(n[i] for i in [3] + list(range(9, 29))):
                erros.append(f"{ident}: cancelada deve ter só os campos da Exceção 1, sem filhos.")
            continue
        usados["part"].add(n[3])
        if n[3] not in participantes:
            erros.append(f"{ident}: participante inexistente.")
        if n[1] == "1" and n[2] == "1":
            erros.append(f"{ident}: saída de terceiros exigiria COD_SIT 08.")
        if not (DT_INI[4:] + DT_INI[2:4] + DT_INI[:2] <= n[9][4:] + n[9][2:4] + n[9][:2] <= n[10][4:] + n[10][2:4] + n[10][:2] <= DT_FIN[4:] + DT_FIN[2:4] + DT_FIN[:2]):
            erros.append(f"{ident}: datas fora do período ou invertidas.")
        if not d["c190"]:
            erros.append(f"{ident}: sem C190.")
        if n[1] == "0" and not d["c170"]:
            erros.append(f"{ident}: entrada de terceiros sem C170.")
        if d["c170"] and sum(numero(i[6]) for i in d["c170"]) != numero(n[15]):
            erros.append(f"{ident}: soma do C170 diverge de VL_MERC.")
        if sum(numero(a[4]) for a in d["c190"]) != numero(n[11]):
            erros.append(f"{ident}: soma de VL_OPR diverge de VL_DOC.")
        if sum(numero(a[5]) for a in d["c190"]) != numero(n[20]):
            erros.append(f"{ident}: soma de VL_BC_ICMS diverge.")
        if sum(numero(a[6]) for a in d["c190"]) != numero(n[21]):
            erros.append(f"{ident}: soma de VL_ICMS diverge.")
        combinacoes = [(a[1], a[2], a[3]) for a in d["c190"]]
        if len(set(combinacoes)) != len(combinacoes):
            erros.append(f"{ident}: C190 repetido para a mesma combinação.")
        for a in d["c190"]:
            if a[2][0] not in ("123" if n[1] == "0" else "567"):
                erros.append(f"{ident}: CFOP {a[2]} incompatível com IND_OPER.")
            if icms(numero(a[5]), numero(a[3])) != numero(a[6]):
                erros.append(f"{ident}: ICMS do C190 {a[2]} não fecha com base e alíquota.")
            if d["c170"]:
                filhos = [i for i in d["c170"] if (i[9], i[10], i[13]) == (a[1], a[2], a[3])]
                if not filhos:
                    erros.append(f"{ident}: combinação do C190 sem C170.")
                if sum(numero(i[12]) for i in filhos) != numero(a[5]) or sum(numero(i[14]) for i in filhos) != numero(a[6]):
                    erros.append(f"{ident}: C190 {a[2]} diverge da soma dos C170.")
        for i in d["c170"]:
            usados["item"].add(i[2])
            usados["unid"].add(i[5])
            usados["nat"].add(i[11])
            if i[2] not in itens or i[5] not in unidades or i[11] not in naturezas:
                erros.append(f"{ident}: C170 com referência inexistente.")
        if n[1] == "0":
            creditos += numero(n[21])
        else:
            debitos += numero(n[21])

    for codigo, r in itens.items():
        usados["unid"].add(r[5])
    if set(participantes) != usados["part"]:
        erros.append(f"0150 não utilizado ou ausente: {set(participantes) ^ usados['part']}")
    if set(itens) - usados["item"]:
        # Saídas próprias não têm C170, mas todo item do 0200 precisa aparecer em algum registro.
        erros.append(f"0200 sem uso: {set(itens) - usados['item']}")
    if unidades != usados["unid"]:
        erros.append(f"0190 sem uso: {unidades ^ usados['unid']}")
    if naturezas != usados["nat"]:
        erros.append(f"0400 sem uso: {naturezas ^ usados['nat']}")

    # Apuração.
    e110 = por("E110")[0]
    if numero(e110[1]) != debitos:
        erros.append(f"VL_TOT_DEBITOS {e110[1]} diverge de {debitos}.")
    if numero(e110[5]) != creditos:
        erros.append(f"VL_TOT_CREDITOS {e110[5]} diverge de {creditos}.")
    saldo = debitos - creditos
    if numero(e110[10]) != max(saldo, Decimal(0)) or numero(e110[13]) != max(-saldo, Decimal(0)):
        erros.append("Saldo apurado ou a transportar não fecha.")
    if numero(e110[12]) != numero(e110[10]) - numero(e110[11]):
        erros.append("VL_ICMS_RECOLHER não fecha.")
    if sum(numero(r[2]) for r in por("E116")) != numero(e110[12]) + numero(e110[14]):
        erros.append("E116 não fecha com VL_ICMS_RECOLHER + DEB_ESP.")
    e100 = por("E100")[0]
    if (e100[1], e100[2]) != (abertura[3], abertura[4]):
        erros.append("E100 não cobre o período do 0000.")
    if por("1001")[0][1] != "0" or len(por("1010")) != 1:
        erros.append("Bloco 1 sem 1010 ou com IND_MOV errado.")
    if not (por("E100") and por("E110")):
        erros.append("Bloco E sem E100 e E110.")

    return erros, {"linhas": len(linhas), "documentos": len(documentos), "debitos": debitos, "creditos": creditos, "recolher": numero(e110[12])}


def main():
    linhas = montar()
    DESTINO.write_bytes("".join("|" + "|".join(c) + "|\r\n" for c in linhas).encode("latin-1"))
    DESTINO.with_suffix(".gabarito.json").write_text(
        json.dumps({"status": "valido", "achados": []}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    erros, resumo = conferir(DESTINO)
    print(f"{DESTINO.relative_to(RAIZ)}: {resumo}")
    if erros:
        print("Conferência com erros:")
        for erro in erros:
            print(f"  - {erro}")
        sys.exit(1)
    print("Conferência sem erros.")


if __name__ == "__main__":
    main()
