# Componentes de terceiros

NITH Converter usa componentes independentes de terceiros. Seus nomes não indicam patrocínio ou autoria do NITH Converter.

## Runtime da aplicação

A publicação Windows x64 inclui runtimes self-contained do .NET e Windows App SDK. Quando necessário, o instalador online obtém o Microsoft Visual C++ Redistributable x64 pelo permalink oficial da Microsoft e o instala silenciosamente.

## ImageMagick

ImageMagick é Copyright ImageMagick Studio LLC e colaboradores e é distribuído sob a licença ImageMagick. O instalador online obtém a versão Windows portátil configurada em `Installer/NithConverter.iss` diretamente da fonte definida pelo projeto e verifica o SHA-256 fixado no script antes da extração.

## FFmpeg

FFmpeg é um projeto independente. O instalador online usa um build Windows x64 LGPL shared do projeto BtbN/FFmpeg-Builds e não seleciona variante marcada `nonfree`. O pacote é mantido privado dentro da pasta do NITH Converter.

## Ghostscript

Ghostscript é Copyright Artifex Software, Inc. O instalador online baixa o instalador x64 oficial configurado em `Installer/NithConverter.iss` e verifica o SHA-256 publicado pela release. O fornecedor oferece Ghostscript sob GNU AGPL e também sob licença comercial. A Nith Digital deve revisar os termos adequados à forma de distribuição/uso do produto.

## Integridade e procedência

O workflow oficial não redistribui esses pacotes dentro do asset `NITH.Converter.exe`. Os componentes são obtidos no computador do usuário durante a instalação. Onde há digest estável publicado (ImageMagick e Ghostscript), o Setup exige o SHA-256 correspondente. O canal de atualização do NITH Converter também publica SHA-256 próprio para o instalador.

Esta relação não concede uma licença para redistribuir a identidade visual ou outros materiais proprietários do NITH Converter.
