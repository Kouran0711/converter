# Componentes de terceiros

NITH Converter executa ImageMagick e FFmpeg como programas externos. Eles são projetos independentes; seus nomes não indicam patrocínio ou autoria do NITH Converter.

## Runtime da aplicação

A publicação Windows x64 inclui os runtimes do .NET e do Windows App SDK. As licenças e avisos específicos dos pacotes restaurados devem acompanhar os arquivos distribuídos.

- .NET: Copyright .NET Foundation and Contributors. [Licença MIT e avisos do runtime](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT).
- Windows App SDK: Copyright Microsoft Corporation. [Licença do código do projeto](https://github.com/microsoft/WindowsAppSDK/blob/main/LICENSE). Os termos dos pacotes binários usados no build também se aplicam.
- Inno Setup, quando usado para gerar o instalador: Copyright Jordan Russell; portions Copyright Martijn Laan. [Licença oficial](https://jrsoftware.org/files/is/license.txt).

## ImageMagick — opcional

Copyright ImageMagick Studio LLC e colaboradores. A [licença ImageMagick](https://imagemagick.org/license/) permite redistribuição com atribuição e cópia da licença. Inclua também os avisos e termos dos delegates presentes no build escolhido. A versão e os arquivos efetivamente incluídos constam em `bundle-manifest.json` quando houver bundle.

## FFmpeg — opcional

FFmpeg é um projeto de seus desenvolvedores; FFmpeg é marca de Fabrice Bellard. Sua licença varia com a configuração: LGPL por padrão, GPL quando componentes GPL são habilitados. Consulte os [termos oficiais](https://ffmpeg.org/legal.html). Um pacote com FFmpeg deve identificar a licença efetiva e incluir seus textos, avisos, configuração e fonte correspondente ao binário. Este projeto não empacota builds `--enable-nonfree`.

## Conteúdo desta entrega

O código-fonte não inclui binários de ImageMagick, FFmpeg ou Ghostscript. Uma publicação comum não baixa nem incorpora esses programas. Um bundle opcional acrescenta os componentes expressamente revisados e seus avisos em `licenses/`; o manifesto identifica cada arquivo por SHA-256.

Esta relação não concede uma licença para redistribuir a logo ou outros materiais do proprietário do NITH Converter.
