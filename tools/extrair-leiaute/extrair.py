#!/usr/bin/env python3
"""Extrai o leiaute 020 da EFD ICMS/IPI do Guia Prático 3.2.4 para o JSON usado pelo motor de validação.

Uso:
    pdftotext -raw -enc UTF-8 "Guia Prático EFD - Versão 3.2.4.pdf" guia.txt
    python tools/extrair-leiaute/extrair.py guia.txt src/Api.Agente/Api.Agente.Domain/Efd/Dados/leiaute-efd-icms-ipi-020.json

O resultado exige revisão manual contra o PDF (T049). Anomalias vão para stderr e o código de saída
é 1 quando há registro sem campos, sem nível ou com numeração de campos quebrada.
"""
import json
import re
import sys

VERSAO_GUIA = "3.2.4"
DOCUMENTO = "Guia Prático da EFD-ICMS/IPI"

BLOCOS = ["0", "B", "C", "D", "E", "G", "H", "K", "1", "9"]

# Campos que só valem a partir de 2027 (Guia, Cap. II, Seção 1, Subseção 16): fora do leiaute 020.
CAMPOS_LEIAUTE_021 = {
    ("C197", "IND_BENEFICIO"), ("C597", "IND_BENEFICIO"), ("D197", "IND_BENEFICIO"), ("D737", "IND_BENEFICIO"),
    ("E111", "IND_BENEFICIO"), ("E220", "IND_BENEFICIO"), ("1921", "IND_BENEFICIO"), ("C180", "COD_PROD"),
}

# Nomes que o PDF quebra em duas linhas ou com espaço: a extração só enxerga a primeira parte.
NOMES_QUEBRADOS = {
    "COD_MU": "COD_MUN",
    "QUANT_BC_COF": "QUANT_BC_COFINS",
    "VL_SLD_CREDOR_TRA": "VL_SLD_CREDOR_TRANSPORTAR",
    "IND_REST_RESSARC": "IND_REST_RESSARC_COMPL_ICMS",
    "NUM_DOC_FI": "NUM_DOC_FIN",
    "NR_": "NR_MEMO",
}

_C100_CANCELADO = ["REG", "IND_OPER", "IND_EMIT", "COD_MOD", "COD_SIT", "SER", "NUM_DOC"]
_C500 = ["REG", "IND_OPER", "IND_EMIT", "COD_PART", "COD_MOD", "COD_SIT", "SER", "NUM_DOC", "DT_DOC"]
_C500_CANCELADO = ["REG", "IND_OPER", "IND_EMIT", "COD_MOD", "COD_SIT", "SER", "NUM_DOC", "DT_DOC"]
_D100_CANCELADO = ["REG", "IND_OPER", "IND_EMIT", "COD_MOD", "COD_SIT", "SER", "SUB", "NUM_DOC"]
_D100 = ["REG", "IND_OPER", "IND_EMIT", "COD_PART", "COD_MOD", "COD_SIT", "SER", "SUB", "NUM_DOC", "DT_DOC"]
_D400 = ["REG", "COD_PART", "COD_MOD", "COD_SIT", "SER", "SUB", "NUM_DOC", "DT_DOC"]

# Situações do documento (COD_SIT) em que o Guia exige só parte dos campos: (situações, obrigatórios, onde está).
# O texto do registro prevalece sobre a coluna de obrigatoriedade. Só afrouxa a exigência de campo vazio;
# as exceções válidas apenas a partir de 2027 (C100, Exceção 12) ficam fora do leiaute 020.
EXCECOES_OBRIGATORIEDADE = {
    "C100": [
        (["02", "03", "04"], _C100_CANCELADO + ["CHV_NFE"], "Exceção 1"),
        (["05"], _C100_CANCELADO, "Exceção 1"),
        (["06", "07"], ["REG", "IND_EMIT", "COD_PART", "COD_MOD", "COD_SIT", "NUM_DOC", "CHV_NFE", "DT_DOC"], "Exceção 3"),
        (["08"], ["REG", "IND_OPER", "IND_EMIT", "COD_PART", "COD_MOD", "COD_SIT", "NUM_DOC", "DT_DOC"], "Exceção 4"),
    ],
    "C500": [
        (["02", "03"], _C500_CANCELADO, "Exceção 1"),
        (["06", "07"], _C500, "Exceção 3"),
        (["08"], _C500, "Exceção 4"),
    ],
    "D100": [
        (["02", "03", "04"], _D100_CANCELADO + ["CHV_CTE"], "Exceção 1"),
        (["05"], _D100_CANCELADO, "Exceção 1"),
        (["06", "07"], _D100 + ["CHV_CTE"], "Exceção 2"),
        (["08"], _D100, "Exceção 3"),
    ],
    "D400": [
        (["02", "03"], ["REG", "COD_SIT", "COD_MOD", "SER", "SUB", "NUM_DOC"], "Exceção 1"),
        (["06", "07"], _D400, "Exceção 2"),
        (["08"], _D400, "Exceção 3"),
    ],
    "D500": [
        (["02", "03"], _C500_CANCELADO, "Exceção 1"),
        (["08"], _C500, "Exceção 2"),
        (["06", "07"], _C500, "Exceção 3"),
    ],
    "D700": [
        (["08"], _C500_CANCELADO, "Exceção 1"),
    ],
    "1500": [
        (["06", "07"], _D100, "Exceção 1"),
        (["08"], _D100, "Exceção 2"),
    ],
}

RE_TITULO = re.compile(r"^REGISTRO ([0-9A-Z]{4})\s*:")
# O nome pode terminar em "*" (MES_REF*, campo com nota de rodapé); o "*" não faz parte do nome.
RE_INICIO_CAMPO = re.compile(r"^(\d{1,2})\s+([A-Z][A-Za-z0-9_]*)\*?(?=\s|$)")
RE_SO_NUMERO = re.compile(r"^\d{1,2}$")
# Opção de lista dentro da descrição ("N - Não"): sem este filtro, parece tipo N sem tamanho.
RE_OPCAO_LISTA = re.compile(r"^[NS]\s*[-–]\s*(?:N[ãa]o|Sim)\b", re.I)
RE_FIM_CAMPO = re.compile(
    # Tipo, tamanho e decimais, seguidos das colunas de obrigatoriedade, que podem ter texto
    # ("Não apresentar", "N (informar no..."). Decimais podem faltar quando há coluna de obrigatoriedade.
    r"(?:^|\s)([CN])\s+(\d{1,5}\*?|-)(?:\s+(\d{1,3}|-))?((?:\s+(?:OC?|N|Não|não|-)(?=\s|$).*)?)\s*$"
)
RE_NIVEL = re.compile(r"N[íi]vel\s+hier[áa]rquico\s*[-–:]?\s*(\d)", re.I)
RE_OCORRENCIA = re.compile(r"Ocorr[êe]ncia\s*[-–:]?\s*(.+)$", re.I)
RE_CABECALHO_PAGINA = re.compile(r"^(Guia Prático EFD-ICMS/IPI|Atualização: |Página \d+ de \d+)")


def normalizar_ocorrencia(texto):
    t = texto.lower()
    if "1:n" in t or "vári" in t or "vari" in t or "0:n" in t:
        return "1:N"
    if "1:1" in t or "0:1" in t or "um" in t or "1 " in t or t.strip() == "1":
        return "1:1"
    return "1:N"


def obrigatorio(colunas):
    """Obrigatório só quando todas as colunas (perfil, entrada/saída) exigem; evita falso erro."""
    valores = colunas.split()
    return "O" if valores and all(v == "O" for v in valores) else "OC"


def analisar(paginas):
    registros, anomalias = [], []
    atual, campo, pendente, numero_solto = None, None, False, None
    acumulado = []
    parcial = None

    def concluir(fim):
        """Grava o campo com o tipo encontrado. Sem hífen explícito, 1 ou 2 dígitos são decimais ("N 02 O")."""
        nonlocal pendente, parcial
        tamanho = fim.group(2)
        decimais = fim.group(3)
        if fim.group(1) == "N" and decimais is None and re.fullmatch(r"\d{1,2}", tamanho):
            tamanho, decimais = "-", tamanho
        decimais = decimais or "-"
        campo.update(
            tipo=fim.group(1),
            tamanho=None if tamanho == "-" else int(tamanho.rstrip("*")),
            tamanhoExato=tamanho.endswith("*"),
            decimais=None if decimais == "-" else int(decimais),
            obrigatorio=obrigatorio(fim.group(4)),
        )
        if (atual["codigo"], campo["nome"]) not in CAMPOS_LEIAUTE_021:
            atual["campos"].append(campo)
        pendente, parcial = False, None

    def fechar():
        nonlocal atual
        if pendente and parcial:
            concluir(parcial)
        if atual is not None:
            registros.append(atual)
        atual = None

    for numero_pagina, texto in enumerate(paginas, 1):
        for bruta in texto.splitlines():
            linha = bruta.strip()
            if not linha or RE_CABECALHO_PAGINA.match(linha):
                continue

            titulo = RE_TITULO.match(linha)
            if titulo:
                fechar()
                atual = {"codigo": titulo.group(1), "pagina": numero_pagina, "campos": [], "nivel": None,
                         "ocorrenciaGuia": None, "lendoCampos": False, "proximo": 1}
                campo, pendente, numero_solto, parcial = None, False, None, None
                continue
            if atual is None:
                continue

            if linha.startswith("Nº") and "Campo" in linha:
                atual["lendoCampos"] = True
                continue

            nivel = RE_NIVEL.search(linha)
            if nivel and atual["nivel"] is None:
                atual["nivel"] = int(nivel.group(1))
                atual["lendoCampos"] = False
            ocorrencia = RE_OCORRENCIA.search(linha)
            if ocorrencia and atual["ocorrenciaGuia"] is None and atual["nivel"] is not None:
                atual["ocorrenciaGuia"] = ocorrencia.group(1).strip()
            if linha.startswith(("Observações", "Observação")):
                atual["lendoCampos"] = False
            if not atual["lendoCampos"]:
                if pendente and parcial:
                    concluir(parcial)
                continue

            # O número do campo pode vir sozinho, com o nome na linha seguinte.
            if RE_SO_NUMERO.match(linha) and int(linha) == atual["proximo"]:
                numero_solto = linha
                continue
            if numero_solto:
                linha, numero_solto = f"{numero_solto} {linha}", None

            inicio = RE_INICIO_CAMPO.match(linha)
            # Só o próximo número inicia campo: descrições também trazem listas numeradas.
            if inicio and int(inicio.group(1)) == atual["proximo"]:
                if pendente and parcial:
                    concluir(parcial)
                elif pendente:
                    anomalias.append(f"{atual['codigo']} (p. {numero_pagina}): campo {campo['numero']} {campo['nome']} sem tipo")
                nome = inicio.group(2)
                campo = {"numero": int(inicio.group(1)), "nome": NOMES_QUEBRADOS.get(nome, nome)}
                pendente = True
                parcial = None
                atual["proximo"] += 1
                acumulado = []
            if pendente and not RE_OPCAO_LISTA.match(linha):
                # Tipo, tamanho e decimais podem vir em linhas separadas.
                acumulado = (acumulado + [linha])[-4:]
                fim = RE_FIM_CAMPO.search(linha) or RE_FIM_CAMPO.search(" ".join(acumulado))
                if fim and fim.group(4).strip():
                    concluir(fim)
                elif fim:
                    # Sem coluna de obrigatoriedade ainda: o resto pode estar na próxima linha ("N", "-", "02 O").
                    parcial = fim
    fechar()

    vistos, unicos = set(), []
    for r in registros:
        if r["campos"] and r["codigo"] not in vistos:
            vistos.add(r["codigo"])
            unicos.append(r)
        elif r["codigo"] not in vistos:
            anomalias.append(f"{r['codigo']} (p. {r['pagina']}): sem campos reconhecidos")

    for r in unicos:
        numeros = [c["numero"] for c in r["campos"]]
        if numeros != list(range(1, len(numeros) + 1)):
            anomalias.append(f"{r['codigo']} (p. {r['pagina']}): numeração de campos {numeros}")
        if r["nivel"] is None:
            anomalias.append(f"{r['codigo']} (p. {r['pagina']}): sem nível hierárquico")

    por_codigo = {r["codigo"]: r for r in unicos}
    for codigo, excecoes in EXCECOES_OBRIGATORIEDADE.items():
        nomes = {c["nome"] for c in por_codigo[codigo]["campos"]}
        if "COD_SIT" not in nomes:
            anomalias.append(f"{codigo}: exceção de obrigatoriedade sem o campo COD_SIT")
        for _, obrigatorios, _ in excecoes:
            for ausente in set(obrigatorios) - nomes:
                anomalias.append(f"{codigo}: exceção de obrigatoriedade cita o campo inexistente {ausente}")
    return unicos, anomalias


def montar(registros):
    saida, pilha = [], {}
    for r in registros:
        nivel = r["nivel"] if r["nivel"] is not None else 2
        pilha[nivel] = r["codigo"]
        for n in list(pilha):
            if n > nivel:
                del pilha[n]
        registro = {
            "codigo": r["codigo"],
            "bloco": r["codigo"][0],
            "nivel": nivel,
            "pai": pilha.get(nivel - 1) if nivel >= 2 else None,
            "ocorrencia": normalizar_ocorrencia(r["ocorrenciaGuia"] or ""),
            "ocorrenciaGuia": r["ocorrenciaGuia"],
            "campos": r["campos"],
            "fonte": {"documento": DOCUMENTO, "versao": VERSAO_GUIA, "localizacao": f"Registro {r['codigo']}, p. {r['pagina']}"},
        }
        if r["codigo"] in EXCECOES_OBRIGATORIEDADE:
            registro["excecoesObrigatoriedade"] = [
                {
                    "situacoes": situacoes,
                    "camposObrigatorios": obrigatorios,
                    "fonte": {"documento": DOCUMENTO, "versao": VERSAO_GUIA, "localizacao": f"Registro {r['codigo']}, {onde}, p. {r['pagina']}"},
                }
                for situacoes, obrigatorios, onde in EXCECOES_OBRIGATORIEDADE[r["codigo"]]
            ]
        saida.append(registro)
    return {
        "codVer": "020",
        "versao": "119",
        "vigencia": {"inicio": "2026-01-01", "fim": "2026-12-31"},
        "codificacao": "ISO-8859-1",
        "fonteVersao": {"documento": "Nota Técnica EFD ICMS IPI 2026.001", "versao": "1.0", "localizacao": "Item 3.1.1, p. 29-30"},
        "blocos": [{"codigo": b, "ordem": i, "abertura": f"{b}001", "encerramento": f"{b}990"} for i, b in enumerate(BLOCOS)],
        "registros": saida,
    }


def main():
    texto = open(sys.argv[1], encoding="utf-8").read()
    registros, anomalias = analisar(texto.split("\f"))
    with open(sys.argv[2], "w", encoding="utf-8", newline="\n") as arquivo:
        json.dump(montar(registros), arquivo, ensure_ascii=False, indent=1)
        arquivo.write("\n")
    print(f"{len(registros)} registros, {sum(len(r['campos']) for r in registros)} campos", file=sys.stderr)
    for a in anomalias:
        print("ANOMALIA", a, file=sys.stderr)
    return 1 if anomalias else 0


if __name__ == "__main__":
    sys.exit(main())
