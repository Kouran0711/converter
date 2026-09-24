# Componentes de terceiros

NITH Converter usa componentes independentes de terceiros. Seus nomes não indicam patrocínio ou autoria do NITH Converter.

## Runtime da aplicação

A publicação Windows x64 inclui runtimes do .NET e Windows App SDK. O setup também transporta o Microsoft Visual C++ Redistributable x64 oficial e assinado para instalação silenciosa quando necessário.

## ImageMagick

ImageMagick é Copyright ImageMagick Studio LLC e colaboradores e é distribuído sob a licença ImageMagick. O build de release baixa o pacote Windows configurado em `scripts/Prepare-Dependencies.ps1`, prepara uma cópia privada dentro do aplicativo e inclui a licença/avisos no diretório `licenses/`.

## FFmpeg

FFmpeg é um projeto independente. O pipeline usa um build Windows x64 marcado como LGPL e inclui texto de licença, referência de fonte e configuração do build. Este projeto não seleciona um build marcado `nonfree`.

## Ghostscript

Ghostscript é Copyright Artifex Software, Inc. O fornecedor oferece Ghostscript sob GNU AGPL e também sob licença comercial. O pipeline inclui informação de licença/fonte junto do runtime. A Nith Digital deve usar os termos adequados à forma de redistribuição escolhida.

## Integridade e procedência

`scripts/Prepare-Dependencies.ps1` usa fontes configuradas explicitamente; onde o provedor/GitHub publica digest SHA-256, o build o confere antes de incorporar o arquivo. O `bundle-manifest.json` gerado registra as versões, origens e SHA-256 dos arquivos efetivamente embarcados.

Esta relação não concede uma licença para redistribuir a identidade visual ou outros materiais proprietários do NITH Converter.
