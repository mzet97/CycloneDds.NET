#
# Copyright(c) 2018 to 2022 ZettaScale Technology and others
#
# This program and the accompanying materials are made available under the
# terms of the Eclipse Public License v. 2.0 which is available at
# http://www.eclipse.org/legal/epl-2.0, or the Eclipse Distribution License
# v. 1.0 which is available at
# http://www.eclipse.org/org/documents/edl-v10.php.
#
# SPDX-License-Identifier: EPL-2.0 OR BSD-3-Clause
#
include(GenerateExportHeader)
configure_file(src/idlc/config.h.in config.h)

set(
  libidljson_hdrs
  include/libidlc/libidlc_generator.h
  ${CMAKE_CURRENT_BINARY_DIR}/config.h)

set(
  libidljson_srcs
  src/libidlc/libidlc__types.h
  src/libidlc/libidlc__descriptor.h
  src/libidlc/libidlc__generator.h
  src/libidlc/model.h
  src/libidlc/libidlc__descriptor.c
  src/libidlc/libidlc__generator.c
  src/libidlc/libidlc__types.c
  src/libidlc/model.c)

add_library(
  libidljson SHARED ${libidljson_srcs} ${libidljson_hdrs})

if(MSVC)
  # ignore warning 5286 and 5287 about enum type conversion
  target_compile_options(libidljson PRIVATE /wd5286 /wd5287)
endif()

generate_export_header(libidljson BASE_NAME LIBIDLC EXPORT_FILE_NAME include/libidlc/libidlc_export.h)

set_target_properties(libidljson PROPERTIES
   OUTPUT_NAME "cycloneddsidljson"
   VERSION ${PROJECT_VERSION}
   SOVERSION ${PROJECT_VERSION_MAJOR}
   C_STANDARD 99)

if(BUILD_TESTING)
  set_property(TARGET libidljson PROPERTY WINDOWS_EXPORT_ALL_SYMBOLS TRUE)
else()
  set_property(TARGET libidljson PROPERTY C_VISIBILITY_PRESET hidden)
endif()

target_include_directories(
  libidljson
  PRIVATE
    include
    include/idlc
    include/generator_common
    ${CMAKE_CURRENT_BINARY_DIR}
    ${CMAKE_CURRENT_BINARY_DIR}/include
  PUBLIC
    "$<BUILD_INTERFACE:${CMAKE_CURRENT_BINARY_DIR}/include/libidlc>"
    "$<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include/libidlc>"
    "$<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/../../core/ddsi/include>"
    "$<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/../../core/cdr/include>")

target_link_libraries(libidljson PRIVATE idl ddsc)

add_library(${PROJECT_NAME}::libidljson ALIAS libidljson)

install(
  TARGETS libidljson
  EXPORT "${CMAKE_PROJECT_NAME}"
  RUNTIME DESTINATION "${CMAKE_INSTALL_BINDIR}" COMPONENT lib
  LIBRARY DESTINATION "${CMAKE_INSTALL_LIBDIR}" COMPONENT lib
  ARCHIVE DESTINATION "${CMAKE_INSTALL_LIBDIR}" COMPONENT lib)

install(
  DIRECTORY
        "$<BUILD_INTERFACE:${CMAKE_CURRENT_BINARY_DIR}/include/libidlc>"
        "${CMAKE_CURRENT_SOURCE_DIR}/include/libidlc"
        "${CMAKE_CURRENT_SOURCE_DIR}/include/idlc"
  DESTINATION "${CMAKE_INSTALL_INCLUDEDIR}"
  COMPONENT dev
  FILES_MATCHING PATTERN "*.h")

# Remove idlc install
# install(
#   TARGETS idlc
# ...

if (INSTALL_PDB)
  # install(FILES $<TARGET_PDB_FILE:idlc>
  #   DESTINATION "${CMAKE_INSTALL_BINDIR}"
  #   COMPONENT dev
  #   OPTIONAL
  # )
endif()

include("${CycloneDDS_SOURCE_DIR}/cmake/Modules/Generate.cmake")

if(BUILD_TESTING)
  add_subdirectory(tests)
endif()

if(BUILD_IDLC_XTESTS)
  add_subdirectory(xtests)
endif()
