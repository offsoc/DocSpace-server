// (c) Copyright Ascensio System SIA 2009-2024
//
// This program is a free software product.
// You can redistribute it and/or modify it under the terms
// of the GNU Affero General Public License (AGPL) version 3 as published by the Free Software
// Foundation. In accordance with Section 7(a) of the GNU AGPL its Section 15 shall be amended
// to the effect that Ascensio System SIA expressly excludes the warranty of non-infringement of
// any third-party rights.
//
// This program is distributed WITHOUT ANY WARRANTY, without even the implied warranty
// of MERCHANTABILITY or FITNESS FOR A PARTICULAR  PURPOSE. For details, see
// the GNU AGPL at: http://www.gnu.org/licenses/agpl-3.0.html
//
// You can contact Ascensio System SIA at Lubanas st. 125a-25, Riga, Latvia, EU, LV-1021.
//
// The  interactive user interfaces in modified source and object code versions of the Program must
// display Appropriate Legal Notices, as required under Section 5 of the GNU AGPL version 3.
//
// Pursuant to Section 7(b) of the License you must retain the original Product logo when
// distributing the program. Pursuant to Section 7(e) we decline to grant you any rights under
// trademark law for use of our trademarks.
//
// All the Product's GUI elements, including illustrations and icon sets, as well as technical
// writing
// content are licensed under the terms of the Creative Commons Attribution-ShareAlike 4.0
// International. See the License terms at http://creativecommons.org/licenses/by-sa/4.0/legalcode

package com.asc.authorization.application.security.jwks;

import com.asc.authorization.application.mapper.KeyPairMapper;
import com.asc.common.core.domain.value.KeyPairType;
import com.nimbusds.jose.jwk.Curve;
import com.nimbusds.jose.jwk.ECKey;
import com.nimbusds.jose.jwk.JWK;
import java.security.*;
import java.security.interfaces.ECPrivateKey;
import java.security.interfaces.ECPublicKey;
import java.security.spec.*;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.context.annotation.Primary;
import org.springframework.stereotype.Component;

/** Generates elliptic curve key pairs and constructs JWKs. */
@Slf4j
@Primary
@Component
@Qualifier("ec")
@RequiredArgsConstructor
public class EcGenerator implements JwksKeyPairGenerator {
  private final KeyPairMapper keyMapper;

  /**
   * Generates an elliptic curve key pair.
   *
   * @return The generated elliptic curve key pair.
   * @throws NoSuchAlgorithmException If the algorithm is not available in the environment.
   */
  public KeyPair generateKeyPair() throws NoSuchAlgorithmException {
    log.info("Generating elliptic curve JWK key pair");

    try {
      var keyPairGenerator = KeyPairGenerator.getInstance("EC");
      keyPairGenerator.initialize(new ECGenParameterSpec("secp256r1"));
      return keyPairGenerator.generateKeyPair();
    } catch (NoSuchAlgorithmException | InvalidAlgorithmParameterException ex) {
      log.error("Could not generate a JWK key pair", ex);
      throw new NoSuchAlgorithmException(ex);
    }
  }

  /**
   * Constructs a JWK from the given public and private keys.
   *
   * @param id The key ID.
   * @param publicKey The public key.
   * @param privateKey The private key.
   * @return The constructed JWK.
   */
  public JWK buildKey(String id, PublicKey publicKey, PrivateKey privateKey) {
    var ecPublicKey = (ECPublicKey) publicKey;
    var ecPrivateKey = (ECPrivateKey) privateKey;
    var curve = Curve.forECParameterSpec(ecPublicKey.getParams());
    return new ECKey.Builder(curve, ecPublicKey).privateKey(ecPrivateKey).keyID(id).build();
  }

  /**
   * Constructs a JWK from the given public and private key strings.
   *
   * @param id The key ID.
   * @param publicKey The public key string.
   * @param privateKey The private key string.
   * @return The constructed JWK.
   * @throws NoSuchAlgorithmException If the algorithm is not available in the environment.
   * @throws InvalidKeySpecException If the key specifications are invalid.
   */
  public JWK buildKey(String id, String publicKey, String privateKey)
      throws NoSuchAlgorithmException, InvalidKeySpecException {
    var ecPublicKey = (ECPublicKey) keyMapper.toPublicKey(publicKey, "EC");
    var ecPrivateKey = (ECPrivateKey) keyMapper.toPrivateKey(privateKey, "EC");
    var curve = Curve.forECParameterSpec(ecPublicKey.getParams());
    return new ECKey.Builder(curve, ecPublicKey).privateKey(ecPrivateKey).keyID(id).build();
  }

  /**
   * Returns the key pair type.
   *
   * @return The key pair type.
   */
  public KeyPairType type() {
    return KeyPairType.EC;
  }
}